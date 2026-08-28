using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;

using Nice3point.Revit.Toolkit.Options;

using Juru.RevitPlugins.Openings.Models.Configs;
using Juru.RevitPlugins.Openings.Models.Placement;
using Juru.RevitPlugins.Openings.Models.Server.Dto;

namespace Juru.RevitPlugins.Openings.Models.Server;

/// <summary>
/// Ставит в активном файле АР или КР задания, загруженные с сервера, и сверяет их между загрузками
/// по <see cref="OpeningFamilyNaming.TaskKeyParam"/>: новое - поставить, изменившееся - переместить
/// и поправить габариты и поворот, удалённое - убрать.
/// </summary>
/// <remarks>
/// Хозяин этих экземпляров - сервер, а не человек: ручная правка положения в файле АР/КР будет
/// возвращена на место следующей загрузкой, см. "Как задания попадают в модель АР и КР"
/// в <c>docs/OpeningsApi.md</c>.
/// </remarks>
public class IncomingOpeningPlacer {
    private readonly Document _doc;
    private readonly OpeningsConfig _config;
    private readonly Dictionary<OpeningType, FamilySymbol> _symbols = [];

    /// <summary>
    /// Типы отверстий, для которых уже пробовали перезагрузить семейство из-за нехватки параметра
    /// в этом запуске - не больше одной перезагрузки на тип за раз, иначе первый же реально
    /// отсутствующий параметр (опечатка в имени, не устаревшее семейство) перезагружал бы семейство
    /// заново на каждом следующем задании этого типа.
    /// </summary>
    private readonly HashSet<OpeningType> _reloadAttempted = [];

    public IncomingOpeningPlacer(Document doc, OpeningsConfig config) {
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// Ставит задания в одной транзакции.
    /// </summary>
    public IncomingPlacementResult Place(IReadOnlyCollection<OpeningTaskViewDto> tasks) {
        if(tasks is null) {
            throw new ArgumentNullException(nameof(tasks));
        }

        var result = new IncomingPlacementResult();
        var (byTaskKey, unkeyed) = GetExistingInstances();
        var projectBasePoint = BasePoint.GetProjectBasePoint(_doc).Position;

        using var transaction = new Transaction(_doc, "Загрузка заданий на отверстия");
        transaction.Start();

        foreach(var task in tasks) {
            bool found = byTaskKey.TryGetValue(task.TaskKey, out var instance);

            if(task.Deleted) {
                if(found) {
                    _doc.Delete(instance.Id);
                    result.Removed++;
                }
                continue;
            }

            if(!found) {
                // Задание без метки рядом с той же точкой - скорее всего, тот же экземпляр,
                // оставшийся от прогона, где TaskKeyParam не записался (семейство было устаревшим
                // или его вовсе не было). Забираем его, а не ставим рядом второй: иначе на каждый
                // такой сбой в прошлом плодился бы дубль без предупреждения, кроме как от самого Revit.
                instance = FindUnkeyedNearby(unkeyed, task, projectBasePoint);
                found = instance is not null;
                unkeyed.Remove(instance);
            }

            if(found) {
                UpdateInstance(instance, task, projectBasePoint, result);
                result.Updated++;
                result.PlacedElements[task.TaskKey] = instance.Id;
            } else if(CreateInstance(task, projectBasePoint, result) is FamilyInstance created) {
                result.Added++;
                result.PlacedElements[task.TaskKey] = created.Id;
            }
        }

        transaction.Commit();
        return result;
    }

    /// <summary>
    /// Индексирует уже стоящие в файле задания. По ключу с сервера - когда параметр записан; отдельно
    /// возвращает и экземпляры без ключа - следы прогона, где записать его не удалось (семейство
    /// было устаревшим), чтобы новая загрузка забирала их, а не ставила дубли рядом. У заданий,
    /// размещённых собственной расстановкой инженера (случай, когда АР и ВИС смотрят на один и тот же
    /// файл), параметр тоже пуст - они физически неотличимы от первого случая, что здесь безопасно:
    /// такой экземпляр всё равно не пришёл бы с сервера дважды под разными taskKey одновременно.
    /// </summary>
    private (Dictionary<string, FamilyInstance> ByTaskKey, List<FamilyInstance> Unkeyed) GetExistingInstances() {
        var familyNames = new HashSet<string>(
            Enum.GetValues(typeof(OpeningType)).Cast<OpeningType>().Select(_config.Naming.GetFamilyName),
            StringComparer.CurrentCultureIgnoreCase);

        var byTaskKey = new Dictionary<string, FamilyInstance>();
        var unkeyed = new List<FamilyInstance>();
        var instances = new FilteredElementCollector(_doc)
            .OfClass(typeof(FamilyInstance))
            .WhereElementIsNotElementType()
            .Cast<FamilyInstance>()
            .Where(instance => instance.Symbol is not null && familyNames.Contains(instance.Symbol.FamilyName));

        foreach(var instance in instances) {
            string taskKey = instance.LookupParameter(_config.Naming.TaskKeyParam)?.AsString();
            if(!string.IsNullOrWhiteSpace(taskKey)) {
                byTaskKey[taskKey] = instance;
            } else if(instance.Location is LocationPoint) {
                unkeyed.Add(instance);
            }
        }

        return (byTaskKey, unkeyed);
    }

    /// <summary>
    /// Допуск по точке при опознании экземпляра без ключа, в мм - тот же, что у собственной
    /// расстановки ВИС (<c>PlacedTasksIndex</c>): позиция не зависит от параметров, которые могли
    /// не записаться, так что настоящий и осиротевший экземпляр должны совпадать почти точно.
    /// </summary>
    private const double _unkeyedPointToleranceMm = 50;

    private FamilyInstance FindUnkeyedNearby(List<FamilyInstance> unkeyed, OpeningTaskViewDto task, XYZ projectBasePoint) {
        if(unkeyed.Count == 0) {
            return null;
        }

        string familyName = _config.Naming.GetFamilyName(task.OpeningType);
        double tolerance = UnitUtils.ConvertToInternalUnits(_unkeyedPointToleranceMm, UnitTypeId.Millimeters);
        var point = GetPoint(task, projectBasePoint);

        return unkeyed.FirstOrDefault(instance =>
            instance.Symbol.FamilyName.Equals(familyName, StringComparison.CurrentCultureIgnoreCase)
            && instance.Location is LocationPoint location
            && location.Point.DistanceTo(point) <= tolerance);
    }

    private FamilyInstance CreateInstance(
        OpeningTaskViewDto task,
        XYZ projectBasePoint,
        IncomingPlacementResult result) {

        var symbol = GetSymbol(task.OpeningType);
        if(symbol is null) {
            result.MissingFamilies.Add(_config.Naming.GetFamilyName(task.OpeningType));
            return null;
        }

        var point = GetPoint(task, projectBasePoint);
        var level = GetLevel(task.LevelName, point.Z);

        if(!symbol.IsActive) {
            symbol.Activate();
            _doc.Regenerate();
        }

        var instance = level is null
            ? _doc.Create.NewFamilyInstance(point, symbol, StructuralType.NonStructural)
            : _doc.Create.NewFamilyInstance(
                point - XYZ.BasisZ * level.ProjectElevation, symbol, level, StructuralType.NonStructural);

        _doc.Regenerate();
        Rotate(instance, point, task.RotationRad);
        SetParameters(instance, task, point, level, result);
        return instance;
    }

    private void UpdateInstance(
        FamilyInstance instance,
        OpeningTaskViewDto task,
        XYZ projectBasePoint,
        IncomingPlacementResult result) {

        var point = GetPoint(task, projectBasePoint);
        var location = (LocationPoint) instance.Location;
        double currentRotation = OpeningAngle.GetCurrent(instance.GetTotalTransform());

        location.Point = point;
        Rotate(instance, point, task.RotationRad - currentRotation);

        _doc.Regenerate();
        var level = GetLevel(task.LevelName, point.Z);
        SetParameters(instance, task, point, level, result);
    }

    private void Rotate(FamilyInstance instance, XYZ point, double angle) {
        if(Math.Abs(angle) < 1e-9) {
            return;
        }

        var axis = Line.CreateBound(point, point + XYZ.BasisZ);
        ElementTransformUtils.RotateElement(_doc, instance.Id, axis, angle);
    }

    /// <summary>
    /// Записывает параметры задания. Если чего-то не нашлось - не сразу сдаётся: возможно, семейство
    /// в проекте устарело, а не действительно лишено параметра. Перезагружает его с сервера семейств
    /// один раз за запуск (сверяться с сервером на каждое задание было бы дорого - см. обсуждение
    /// в памяти проекта) и пробует записать те же параметры заново на том же экземпляре: перезагрузка
    /// с перезаписью значений типов обновляет уже стоящие в проекте экземпляры на месте, отдельно
    /// пересоздавать их не нужно.
    /// </summary>
    private void SetParameters(
        FamilyInstance instance,
        OpeningTaskViewDto task,
        XYZ point,
        Level level,
        IncomingPlacementResult result) {

        var missing = WriteParameters(instance, task, point, level);

        if(missing.Count > 0 && _reloadAttempted.Add(task.OpeningType)) {
            string familyName = _config.Naming.GetFamilyName(task.OpeningType);
            if(ReloadFromLibrary(familyName)) {
                _doc.Regenerate();
                missing = WriteParameters(instance, task, point, level);
            }
        }

        foreach(string paramName in missing) {
            result.MissingParameters.Add(paramName);
        }
    }

    private List<string> WriteParameters(FamilyInstance instance, OpeningTaskViewDto task, XYZ point, Level level) {
        var naming = _config.Naming;
        var missing = new List<string>();

        void Length(string paramName, double feetValue) {
            if(!ParameterWriter.SetLength(instance, paramName, feetValue)) {
                missing.Add(paramName);
            }
        }

        void Text(string paramName, string value) {
            if(!ParameterWriter.SetText(instance, paramName, value)) {
                missing.Add(paramName);
            }
        }

        bool isRound = task.OpeningType is OpeningType.WallRound or OpeningType.FloorRound;
        if(isRound) {
            Length(naming.DiameterParam, ToFeet(task.DiameterMm ?? 0));
        } else {
            Length(naming.WidthParam, ToFeet(task.WidthMm ?? 0));
            Length(naming.HeightParam, ToFeet(task.HeightMm ?? 0));
        }
        Length(naming.ThicknessParam, ToFeet(task.ThicknessMm));

        double centerElevation = GetCenterElevation(task, point);
        // Отметка оси необязательна: если её в семействе нет, в отчёт это не пишем.
        ParameterWriter.SetLength(instance, naming.CenterOffsetParam, centerElevation);
        Length(naming.ElevationParam, point.Z);
        if(level is not null) {
            Length(naming.LevelOffsetParam, point.Z - level.ProjectElevation);
        }

        Text(naming.MepSystemParam, task.MepSystem);
        Text(naming.DescriptionParam, task.Description);
        Text(naming.DateParam, task.SentAt.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture));
        Text(naming.AuthorParam, string.IsNullOrWhiteSpace(task.SentByDisplayName) ? task.SentBy : task.SentByDisplayName);
        // TaskKeyParam через тот же Text(), что и остальные: без него сверка между загрузками вообще
        // не работает (каждое задание считалось бы новым), поэтому его нехватка обязана и вызывать
        // перезагрузку семейства наравне с прочими параметрами, и быть видна в отчёте - раньше была
        // тихой, и именно поэтому одно-единственное семейство без этого параметра осталось незамеченным
        // через несколько прогонов.
        Text(naming.TaskKeyParam, task.TaskKey);

        return missing;
    }

    /// <summary>
    /// Отметки низа и оси отверстия по точке вставки - то же правило, что и у выдачи задания
    /// в файле ВИС (<c>OpeningTaskPlacer.GetElevations</c>): начало координат у семейств разное.
    /// </summary>
    private double GetCenterElevation(OpeningTaskViewDto task, XYZ point) {
        switch(task.OpeningType) {
            case OpeningType.WallRectangle:
                return point.Z + ToFeet(task.HeightMm ?? 0) / 2;
            case OpeningType.WallRound:
                return point.Z;
            default:
                return point.Z - ToFeet(task.ThicknessMm) / 2;
        }
    }

    private XYZ GetPoint(OpeningTaskViewDto task, XYZ projectBasePoint) {
        var meters = task.Location;
        var relative = new XYZ(
            UnitUtils.ConvertToInternalUnits(meters.X, UnitTypeId.Meters),
            UnitUtils.ConvertToInternalUnits(meters.Y, UnitTypeId.Meters),
            UnitUtils.ConvertToInternalUnits(meters.Z, UnitTypeId.Meters));
        return projectBasePoint + relative;
    }

    private FamilySymbol GetSymbol(OpeningType openingType) {
        if(_symbols.TryGetValue(openingType, out var cached)) {
            return cached;
        }

        string familyName = _config.Naming.GetFamilyName(openingType);
        var symbol = FindSymbol(familyName);

        // В проекте семейства нет вовсе - догружаем эталон с сервера семейств. Версию, уже стоящую
        // в проекте, не трогаем и не сравниваем с эталонной: перезаписывать типы без явного действия
        // пользователя не должны, только восполнять то, чего действительно нет.
        if(symbol is null) {
            symbol = LoadFromLibrary(familyName);
        }

        _symbols[openingType] = symbol;
        return symbol;
    }

    private FamilySymbol FindSymbol(string familyName) {
        return new FilteredElementCollector(_doc)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .FirstOrDefault(item => item.FamilyName.Equals(familyName, StringComparison.CurrentCultureIgnoreCase));
    }

    private FamilySymbol LoadFromLibrary(string familyName) {
        string message = $"Семейства {familyName} нет в проекте - загружаю эталон с сервера семейств:\n{{0}}";
        return LoadFamilyFile(familyName, message, out var family) ? FindSymbol(family.Name) : null;
    }

    /// <summary>
    /// Перезагружает уже стоящее в проекте семейство эталоном с сервера семейств, перезаписывая
    /// значения его типов - у экземпляров, уже размещённых в проекте, появятся все параметры
    /// эталона без пересоздания. Вызывается только когда обычная запись параметра не удалась:
    /// сверяться с сервером на каждое задание было бы дорого без причины.
    /// </summary>
    private bool ReloadFromLibrary(string familyName) {
        string message = $"В семействе {familyName} не нашлось нужных параметров - "
            + "перезагружаю его эталоном с сервера семейств:\n{0}";
        return LoadFamilyFile(familyName, message, out _);
    }

    private bool LoadFamilyFile(string familyName, string messageFormat, out Family family) {
        family = null;
        string libraryPath = _config.FamilyLibraryPath;
        if(string.IsNullOrWhiteSpace(libraryPath)) {
            return false;
        }

        string familyPath = Path.Combine(libraryPath, familyName + ".rfa");
        if(!File.Exists(familyPath)) {
            return false;
        }

        TaskDialog.Show("Загрузка заданий", string.Format(messageFormat, familyPath));

        // FamilyLoadOptions() по умолчанию перезаписывает значения параметров уже существующих
        // типов - ровно то, что нужно и для впервые загружаемого семейства (перезаписывать нечего),
        // и для устаревшего (подхватит недостающие параметры на уже стоящих экземплярах).
        return _doc.LoadFamily(familyPath, new FamilyLoadOptions(), out family);
    }

    /// <summary>
    /// Возвращает уровень по имени с сервера, а если такого уровня в файле нет - ближайший снизу.
    /// </summary>
    private Level GetLevel(string levelName, double pointZ) {
        var levels = new FilteredElementCollector(_doc).OfClass(typeof(Level)).Cast<Level>().ToList();

        var byName = levels.FirstOrDefault(
            level => level.Name.Equals(levelName, StringComparison.CurrentCultureIgnoreCase));
        if(byName is not null) {
            return byName;
        }

        return levels
            .Where(level => level.ProjectElevation <= pointZ)
            .OrderByDescending(level => level.ProjectElevation)
            .FirstOrDefault();
    }

    private static double ToFeet(double millimeters) {
        return UnitUtils.ConvertToInternalUnits(millimeters, UnitTypeId.Millimeters);
    }
}
