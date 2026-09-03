using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Principal;

using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

using Juru.RevitPlugins.Openings.Models.Configs;
using Juru.RevitPlugins.Openings.Models.Intersections;

namespace Juru.RevitPlugins.Openings.Models.Placement;

/// <summary>
/// Размещает задания на отверстия по рассчитанным отверстиям.
/// </summary>
public class OpeningTaskPlacer {
    private readonly Document _doc;
    private readonly OpeningsConfig _config;
    private readonly OpeningFamilyLibrary _library;
    private readonly Dictionary<OpeningType, FamilySymbol> _symbols = [];

    public OpeningTaskPlacer(Document doc, OpeningsConfig config) {
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _library = new OpeningFamilyLibrary(doc, config.FamilyLibraryPath, "Расстановка заданий на отверстия");
    }

    /// <summary>
    /// Размещает задания в одной транзакции. Отверстие, которое разместить не удалось,
    /// не прерывает расстановку, а попадает в отчёт.
    /// </summary>
    public PlacementResult Place(IReadOnlyCollection<PlannedOpening> openings) {
        if(openings is null) { throw new ArgumentNullException(nameof(openings)); }

        var result = new PlacementResult();
        var placedTasks = new PlacedTasksIndex(_doc, _config);

        using var transaction = new Transaction(_doc, "Расстановка заданий на отверстия");
        transaction.Start();

        foreach(var opening in openings) {
            try {
                var instance = PlaceOne(opening, result, placedTasks, out string failure);
                if(instance is null) {
                    // Пропуск дубля - не отказ: причины у него нет, он ожидаемый.
                    if(failure is not null) {
                        result.Failed.Add(new IntersectionRejection(opening.Primary, failure));
                    }
                } else {
                    result.Placed.Add(instance);
                }
            } catch(Autodesk.Revit.Exceptions.ApplicationException exception) {
                result.Failed.Add(new IntersectionRejection(opening.Primary, exception.Message));
            }
        }

        transaction.Commit();

        result.UpdatedFamilies.UnionWith(_library.UpdatedFamilies);
        result.LoadedFamilies.UnionWith(_library.LoadedFamilies);
        return result;
    }

    private FamilyInstance PlaceOne(
        PlannedOpening opening,
        PlacementResult result,
        PlacedTasksIndex placedTasks,
        out string failure) {

        failure = null;
        var size = opening.GetSize();
        var symbol = GetSymbol(size.OpeningType);
        if(symbol is null) {
            failure = $"Семейство {_config.Naming.GetFamilyName(size.OpeningType)} не загружено в проект.";
            return null;
        }

        var level = GetLevel(opening.Primary.Mep.Element, size.Center);
        var point = InsertionPoint.Get(size, opening.Primary, GetElevationRounding(opening));

        // Тот же инженерный элемент уже когда-то дал задание - опознано не по близости точки,
        // а по id элемента в описании (OpeningSourceDescription): труба сдвинулась и всё ещё
        // сдвинулась в пределах того же прохода, но результат мог уехать больше чем на допуск
        // точки. Такое задание не дублируется, а двигается на месте - см. память проекта,
        // "обновление на месте вместо orphan-plus-duplicate".
        var existing = placedTasks.FindBySourceMep(opening.Sources.Select(source => source.Mep.Element.Id));
        if(existing is not null) {
            if(placedTasks.MatchesExisting(existing, point, size.BoxVolume)) {
                // Не сдвинулось за пределы допуска - это то же самое задание на том же месте.
                result.SkippedAsDuplicate++;
                return null;
            }

            return UpdateInstance(existing, placedTasks, opening, size, point, level, result);
        }

        if(placedTasks.AlreadyPlaced(point, size.BoxVolume)) {
            result.SkippedAsDuplicate++;
            return null;
        }

        var instance = CreateInstance(symbol, point, level);
        if(instance is null) {
            failure = "Revit не создал экземпляр семейства задания.";
            return null;
        }

        // Параметры назначаются после Regenerate: до него значения, назначенные только что созданному
        // экземпляру, могут не примениться.
        _doc.Regenerate();
        SetParameters(instance, opening, size, point, level, result);

        double angle = OpeningAngle.Get(opening.Primary);
        if(Math.Abs(angle) > 1e-9) {
            var axis = Line.CreateBound(point, point + XYZ.BasisZ);
            ElementTransformUtils.RotateElement(_doc, instance.Id, axis, angle);
        }

        // Задание учитывается сразу: иначе два отверстия в одном месте внутри одного прогона
        // дали бы два задания.
        placedTasks.Add(instance, point, size.BoxVolume);
        return instance;
    }

    /// <summary>
    /// Двигает уже стоящее задание на новую точку вместо того, чтобы оставить его сиротой
    /// и поставить рядом новое - тот же экземпляр, тот же <c>UniqueId</c> (и, значит, тот же
    /// <c>taskKey</c> на сервере), просто на новом месте и с новыми габаритами.
    /// </summary>
    private FamilyInstance UpdateInstance(
        FamilyInstance instance,
        PlacedTasksIndex placedTasks,
        PlannedOpening opening,
        OpeningSize size,
        XYZ point,
        Level level,
        PlacementResult result) {

        placedTasks.Remove(instance);

        var location = (LocationPoint) instance.Location;
        double currentAngle = OpeningAngle.GetCurrent(instance.GetTotalTransform());
        location.Point = point;

        double targetAngle = OpeningAngle.Get(opening.Primary);
        double delta = targetAngle - currentAngle;
        if(Math.Abs(delta) > 1e-9) {
            var axis = Line.CreateBound(point, point + XYZ.BasisZ);
            ElementTransformUtils.RotateElement(_doc, instance.Id, axis, delta);
        }

        _doc.Regenerate();
        SetParameters(instance, opening, size, point, level, result);

        placedTasks.Add(instance, point, size.BoxVolume);
        result.Updated++;
        return instance;
    }

    private FamilyInstance CreateInstance(FamilySymbol symbol, XYZ point, Level level) {
        if(!symbol.IsActive) {
            symbol.Activate();
            _doc.Regenerate();
        }

        if(level is null) {
            return _doc.Create.NewFamilyInstance(point, symbol, StructuralType.NonStructural);
        }

        // Семейство размещается на уровне, поэтому точка задаётся относительно отметки уровня.
        var pointOnLevel = point - XYZ.BasisZ * level.ProjectElevation;
        return _doc.Create.NewFamilyInstance(pointOnLevel, symbol, level, StructuralType.NonStructural);
    }

    /// <summary>
    /// Возвращает округление отметки для отверстия в мм.
    /// </summary>
    /// <remarks>
    /// У объединённого отверстия шаг свой: его габариты уже сложены из нескольких проходов и
    /// переокруглены мельче, и отметка должна лечь на тот же шаг, что и они.
    /// </remarks>
    private int GetElevationRounding(PlannedOpening opening) {
        return opening.IsUnited
            ? _config.UnitedTaskSizeRoundingMm
            : opening.Primary.Settings.ElevationRoundingMm;
    }

    /// <summary>
    /// Записывает параметры задания. Если чего-то не нашлось - не сразу сдаётся: возможно, семейство
    /// в проекте устарело, а не действительно лишено параметра. Тогда семейство один раз за запуск
    /// обновляется эталоном с сервера семейств (с уведомлением) и те же параметры пишутся заново
    /// на том же экземпляре: перезагрузка с перезаписью значений типов обновляет уже стоящие
    /// в проекте экземпляры на месте, пересоздавать их не нужно.
    /// </summary>
    private void SetParameters(
        FamilyInstance instance,
        PlannedOpening opening,
        OpeningSize size,
        XYZ point,
        Level level,
        PlacementResult result) {

        var missing = WriteParameters(instance, opening, size, point, level);

        if(missing.Count > 0 && _library.ReloadOutdated(_config.Naming.GetFamilyName(size.OpeningType))) {
            _doc.Regenerate();
            missing = WriteParameters(instance, opening, size, point, level);
        }

        foreach(string paramName in missing) {
            result.MissingParameters.Add(paramName);
        }
    }

    private List<string> WriteParameters(
        FamilyInstance instance,
        PlannedOpening opening,
        OpeningSize size,
        XYZ point,
        Level level) {

        var naming = _config.Naming;
        var missing = new List<string>();

        // Отсутствие параметра в семействе не срывает расстановку, но должно попасть в отчёт:
        // иначе задание встанет без данных, и узнается это только на чертеже.
        void Length(string paramName, double value) {
            if(!ParameterWriter.SetLength(instance, paramName, value)) {
                missing.Add(paramName);
            }
        }

        void Text(string paramName, string value) {
            if(!ParameterWriter.SetText(instance, paramName, value)) {
                missing.Add(paramName);
            }
        }

        void Flag(string paramName, int value) {
            if(!ParameterWriter.SetInteger(instance, paramName, value)) {
                missing.Add(paramName);
            }
        }

        if(size.IsRound) {
            Length(naming.DiameterParam, size.Diameter);
        } else {
            Length(naming.WidthParam, size.Width);
            Length(naming.HeightParam, size.Height);
        }
        Length(naming.ThicknessParam, size.Thickness);

        // Отметки считаются от точки вставки, а не от расчётного центра: точка вставки округлена,
        // и отметка в параметре должна совпадать с тем, где отверстие действительно стоит.
        var (_, centerElevation) = GetElevations(size, point);
        // Отметка оси необязательна: если её в семействе нет, в отчёт это не пишем.
        ParameterWriter.SetLength(instance, naming.CenterOffsetParam, centerElevation);
        Length(naming.ElevationParam, point.Z);
        if(level is not null) {
            Length(naming.LevelOffsetParam, point.Z - level.ProjectElevation);
        }

        Text(naming.MepSystemParam, GetMepSystems(opening));
        Text(naming.DescriptionParam, GetDescription(opening));
        Text(naming.DateParam, DateTime.Now.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture));
        Text(naming.AuthorParam, GetAuthor());
        Flag(naming.ManuallyPlacedParam, 0);
        Flag(naming.UnitedParam, opening.IsUnited ? 1 : 0);

        return missing;
    }

    /// <summary>
    /// Возвращает отметки низа и оси отверстия по его точке вставки.
    /// </summary>
    /// <remarks>
    /// Начало координат у семейств разное, поэтому и отметки выводятся из точки по-разному:
    /// у прямоугольного отверстия в стене точка лежит на низу, у круглого - на оси,
    /// у отверстия в перекрытии - на верхней грани плиты. Расхождение здесь означает отметку
    /// в спецификации, не совпадающую с моделью.
    /// </remarks>
    private (double Bottom, double Center) GetElevations(OpeningSize size, XYZ point) {
        switch(size.OpeningType) {
            case OpeningType.WallRectangle:
                return (point.Z, point.Z + size.Height / 2);
            case OpeningType.WallRound:
                return (point.Z - size.Diameter / 2, point.Z);
            default:
                return (point.Z - size.Thickness, point.Z - size.Thickness / 2);
        }
    }

    /// <summary>
    /// Возвращает описание задания: файлы, имена и id элементов, из пересечения которых оно возникло.
    /// У объединённого задания перечисляются все, иначе после склейки не найти, откуда оно взялось.
    /// </summary>
    private string GetDescription(PlannedOpening opening) {
        return OpeningSourceDescription.Write(
            opening.Sources.Select(source => (source.Mep.Element, source.StructureElement)));
    }

    /// <summary>
    /// Возвращает имена инженерных систем всех проходов задания без повторов.
    /// </summary>
    private string GetMepSystems(PlannedOpening opening) {
        var systems = opening.Sources
            .Select(source => GetMepSystemName(source.Mep.Element))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct();
        return string.Join("; ", systems);
    }

    private string GetMepSystemName(Element element) {
        return element.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString()
            ?? element.get_Parameter(BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM)?.AsValueString()
            ?? string.Empty;
    }

    /// <summary>
    /// Возвращает учётную запись Windows: имя пользователя Autodesk у каждого своё
    /// и с сотрудником в отчётах не сопоставляется.
    /// </summary>
    private string GetAuthor() {
        try {
            return WindowsIdentity.GetCurrent().Name;
        } catch(Exception) {
            return Environment.UserName;
        }
    }

    private FamilySymbol GetSymbol(OpeningType openingType) {
        if(_symbols.TryGetValue(openingType, out var cached)) {
            return cached;
        }

        string familyName = _config.Naming.GetFamilyName(openingType);
        var symbol = _library.FindSymbol(familyName);

        // Семейства нет в проекте вовсе - догружаем эталон, иначе задание некуда ставить.
        symbol ??= _library.LoadMissing(familyName);

        _symbols[openingType] = symbol;
        return symbol;
    }

    /// <summary>
    /// Возвращает уровень инженерного элемента, а если его нет - ближайший уровень снизу.
    /// </summary>
    private Level GetLevel(Element mepElement, XYZ point) {
        if(_doc.GetElement(mepElement.LevelId) is Level ownLevel) {
            return ownLevel;
        }

        return new FilteredElementCollector(_doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .Where(level => level.ProjectElevation <= point.Z)
            .OrderByDescending(level => level.ProjectElevation)
            .FirstOrDefault();
    }
}
