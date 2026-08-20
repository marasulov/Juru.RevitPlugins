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
/// Размещает задания на отверстия по найденным пересечениям.
/// </summary>
public class OpeningTaskPlacer {
    private readonly Document _doc;
    private readonly OpeningsConfig _config;
    private readonly OpeningSizeCalculator _sizeCalculator = new OpeningSizeCalculator();
    private readonly Dictionary<OpeningType, FamilySymbol> _symbols = [];

    public OpeningTaskPlacer(Document doc, OpeningsConfig config) {
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// Размещает задания в одной транзакции. Пересечение, для которого разместить задание не удалось,
    /// не прерывает расстановку, а попадает в отчёт.
    /// </summary>
    public PlacementResult Place(IReadOnlyCollection<MepStructureIntersection> intersections) {
        if(intersections is null) { throw new ArgumentNullException(nameof(intersections)); }

        var result = new PlacementResult();
        var placedTasks = new PlacedTasksIndex(_doc, _config);
        using var transaction = new Transaction(_doc, "Расстановка заданий на отверстия");
        transaction.Start();

        foreach(var intersection in intersections) {
            try {
                var instance = PlaceOne(intersection, result, placedTasks, out string failure);
                if(instance is null) {
                    // Пропуск дубля - не отказ: причины у него нет, он ожидаемый.
                    if(failure is not null) {
                        result.Failed.Add(new IntersectionRejection(intersection, failure));
                    }
                } else {
                    result.Placed.Add(instance);
                }
            } catch(Autodesk.Revit.Exceptions.ApplicationException exception) {
                result.Failed.Add(new IntersectionRejection(intersection, exception.Message));
            }
        }

        transaction.Commit();
        return result;
    }

    private FamilyInstance PlaceOne(
        MepStructureIntersection intersection,
        PlacementResult result,
        PlacedTasksIndex placedTasks,
        out string failure) {
        failure = null;
        var size = _sizeCalculator.Calculate(intersection);
        if(size is null) {
            failure = "Габариты отверстия по геометрии пересечения не определены либо недопустимо малы.";
            return null;
        }

        var symbol = GetSymbol(size.OpeningType);
        if(symbol is null) {
            failure = $"Семейство {_config.Naming.GetFamilyName(size.OpeningType)} не загружено в проект.";
            return null;
        }

        var level = GetLevel(intersection.Mep.Element, size.Center);
        var point = InsertionPoint.Get(size, intersection);

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
        SetParameters(instance, intersection, size, level, result);

        double angle = OpeningAngle.Get(intersection);
        if(Math.Abs(angle) > 1e-9) {
            var axis = Line.CreateBound(point, point + XYZ.BasisZ);
            ElementTransformUtils.RotateElement(_doc, instance.Id, axis, angle);
        }

        // Задание учитывается сразу: иначе два пересечения в одном месте внутри одного прогона
        // дали бы два задания.
        placedTasks.Add(point, size.BoxVolume);
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

    private void SetParameters(
        FamilyInstance instance,
        MepStructureIntersection intersection,
        OpeningSize size,
        Level level,
        PlacementResult result) {

        var naming = _config.Naming;

        // Отсутствие параметра в семействе не срывает расстановку, но должно попасть в отчёт:
        // иначе задание встанет без данных, и узнается это только на чертеже.
        void Length(string paramName, double value) {
            if(!ParameterWriter.SetLength(instance, paramName, value)) {
                result.MissingParameters.Add(paramName);
            }
        }

        void Text(string paramName, string value) {
            if(!ParameterWriter.SetText(instance, paramName, value)) {
                result.MissingParameters.Add(paramName);
            }
        }

        void Flag(string paramName, int value) {
            if(!ParameterWriter.SetInteger(instance, paramName, value)) {
                result.MissingParameters.Add(paramName);
            }
        }

        if(size.IsRound) {
            Length(naming.DiameterParam, size.Diameter);
        } else {
            Length(naming.WidthParam, size.Width);
            Length(naming.HeightParam, size.Height);
        }
        Length(naming.ThicknessParam, size.Thickness);

        double halfHeight = size.IsRound ? size.Diameter / 2 : size.Height / 2;
        double bottomElevation = size.Center.Z - halfHeight;
        // Отметка оси необязательна: если её в семействе нет, в отчёт это не пишем.
        ParameterWriter.SetLength(instance, naming.CenterOffsetParam, size.Center.Z);
        Length(naming.BottomOffsetParam, bottomElevation);
        if(level is not null) {
            Length(naming.LevelOffsetParam, bottomElevation - level.ProjectElevation);
        }

        Text(naming.MepSystemParam, GetMepSystemName(intersection.Mep.Element));
        Text(naming.DescriptionParam, GetDescription(intersection));
        Text(naming.DateParam, DateTime.Now.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture));
        Text(naming.AuthorParam, GetAuthor());
        Flag(naming.ManuallyPlacedParam, 0);
        Flag(naming.UnitedParam, 0);
    }

    /// <summary>
    /// Возвращает описание задания: файлы, имена и id элементов, из пересечения которых оно возникло.
    /// Этого достаточно, чтобы найти источник задания без отдельного идентификатора.
    /// </summary>
    private string GetDescription(MepStructureIntersection intersection) {
        var mep = intersection.Mep.Element;
        var structure = intersection.StructureElement;
        return string.Join("; ",
            $"{mep.Document.Title}: {mep.Name}: {mep.Id}",
            $"{structure.Document.Title}: {structure.Name}: {structure.Id}");
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
        var symbol = new FilteredElementCollector(_doc)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .FirstOrDefault(item => item.FamilyName.Equals(familyName, StringComparison.CurrentCultureIgnoreCase));

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
