using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

using Juru.RevitPlugins.Core.FileNaming;
using Juru.RevitPlugins.Core.Localization;
using Juru.RevitPlugins.Openings.Models.Configs;
using Juru.RevitPlugins.Openings.Models.Intersections;
using Juru.RevitPlugins.Openings.Models.Placement;

namespace Juru.RevitPlugins.Openings.Commands;

/// <summary>
/// Объединяет вручную выбранные задания на отверстия в одно - для случаев, которые
/// авторасстановка не склеила сама (соседние проходы чуть дальше допуска и т.п.).
/// </summary>
/// <remarks>
/// Не пересчитывает объединение с нуля: находит исходные инженерные элементы выбранных заданий
/// по <see cref="OpeningSourceDescription.ParseMepElementIds"/> и заново проводит их через тот же
/// пайплайн пересечений, что и обычная расстановка (<see cref="SolidIntersectionFinder"/>,
/// <see cref="IntersectionValidator"/>, <see cref="OpeningSizeCalculator"/>), а группировку не
/// доверяет автоматическому <see cref="OpeningMerger"/> (тот бы не склеил то, что не касается) -
/// вместо этого напрямую зовёт <see cref="PlannedOpening.Unite"/> на всех полученных отверстиях
/// сразу, форсируя ровно ту группу, что выбрал пользователь. Размещает через тот же
/// <see cref="OpeningTaskPlacer"/>, что и обычная расстановка - геометрия и параметры вычисляются
/// одинаково, разница только в способе получить группу для склейки.
/// </remarks>
[Transaction(TransactionMode.Manual)]
public class UniteOpeningTasksCommand : IExternalCommand {
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) {
        var uiDocument = commandData.Application.ActiveUIDocument;
        var doc = uiDocument.Document;
        var config = OpeningsConfig.GetOpeningsConfig();
        var culture = PluginCulture.Resolve(config, commandData.Application.Application.Language);
        var localization = PluginLocalization.Load(typeof(UniteOpeningTasksCommand), culture);

        if(!new DisciplineResolver(config).TryResolve(doc.PathName, out var discipline, out _)
            || !discipline.IssuesOpeningTasks()) {
            TaskDialog.Show(
                localization.GetString("Unite.DialogTitle"),
                localization.GetString("Unite.NotMepFileError"));
            return Result.Cancelled;
        }

        var familyNames = new HashSet<string>(
            Enum.GetValues(typeof(OpeningType)).Cast<OpeningType>().Select(config.Naming.GetFamilyName),
            StringComparer.CurrentCultureIgnoreCase);

        IList<Reference> picked;
        try {
            picked = uiDocument.Selection.PickObjects(
                ObjectType.Element,
                new OpeningTaskSelectionFilter(familyNames),
                localization.GetString("Unite.SelectHint"));
        } catch(Autodesk.Revit.Exceptions.OperationCanceledException) {
            return Result.Cancelled;
        }

        var instances = picked
            .Select(reference => doc.GetElement(reference) as FamilyInstance)
            .Where(instance => instance is not null)
            .ToList();

        if(instances.Count < 2) {
            TaskDialog.Show(
                localization.GetString("Unite.DialogTitle"),
                localization.GetString("Unite.SelectAtLeastTwo"));
            return Result.Cancelled;
        }

        if(!TryGetSingleStructureKind(instances, config, out var structureKind)) {
            TaskDialog.Show(
                localization.GetString("Unite.DialogTitle"),
                localization.GetString("Unite.MixedSelectionError"));
            return Result.Cancelled;
        }

        var mepIds = GetSourceMepIds(doc, instances, config);
        if(mepIds.Count == 0) {
            TaskDialog.Show(
                localization.GetString("Unite.DialogTitle"),
                localization.GetString("Unite.MissingSourceElementsError"));
            return Result.Cancelled;
        }

        var linksProvider = new StructureLinksProvider(doc, config);
        var structureProviders = linksProvider.GetProviders();
        if(structureProviders.Count == 0) {
            TaskDialog.Show(
                localization.GetString("Unite.DialogTitle"),
                localization.GetString("Unite.NoLinksError"));
            return Result.Cancelled;
        }

        var mepProvider = new MepElementsProvider(doc, config, mepIds);
        var intersections = new SolidIntersectionFinder(mepProvider).Find(structureProviders);
        var validation = new IntersectionValidator(config).Validate(intersections);

        var calculator = new OpeningSizeCalculator();
        var planned = validation.Accepted
            .Select(calculator.Calculate)
            .Where(opening => opening is not null && opening.StructureKind == structureKind)
            .ToList();

        if(planned.Count == 0) {
            TaskDialog.Show(
                localization.GetString("Unite.DialogTitle"),
                localization.GetString("Unite.RecalculateFailedError"));
            return Result.Cancelled;
        }

        double sameSizeTolerance = UnitUtils.ConvertToInternalUnits(
            config.UnitedTaskSizeRoundingMm, UnitTypeId.Millimeters);
        var merged = PlannedOpening.Unite(planned, sameSizeTolerance, sameSizeTolerance);

        var placement = new OpeningTaskPlacer(doc, config).Place([merged]);
        if(placement.Placed.Count == 0) {
            string reasons = string.Join("\n", placement.Failed.Select(failure => failure.Reason));
            TaskDialog.Show(
                localization.GetString("Unite.DialogTitle"),
                localization.GetString("Unite.PlaceFailedFormat", reasons.Length > 0 ? "\n" + reasons : ""));
            return Result.Failed;
        }

        // OpeningTaskPlacer.Place сам сканирует все стоящие в модели задания и опознаёт тот же
        // инженерный элемент по описанию (см. "обновление на месте") - результат объединения вполне
        // мог оказаться не новым экземпляром, а одним из выбранных, обновлённым на месте. Такой
        // исходник удалять нельзя - иначе после удаления результат остаётся невалидным объектом.
        var placedIds = new HashSet<ElementId>(placement.Placed.Select(item => item.Id));

        using(var transaction = new Transaction(doc, localization.GetString("Unite.RemoveSourcesTransactionName"))) {
            transaction.Start();
            foreach(var instance in instances) {
                if(!placedIds.Contains(instance.Id) && doc.GetElement(instance.Id) is not null) {
                    doc.Delete(instance.Id);
                }
            }
            transaction.Commit();
        }

        uiDocument.Selection.SetElementIds(placedIds.ToList());
        TaskDialog.Show(
            localization.GetString("Unite.DialogTitle"),
            localization.GetString("Unite.SuccessFormat", instances.Count));
        return Result.Succeeded;
    }

    /// <summary>
    /// Проверяет, что все выбранные задания одного вида конструкции (стена или перекрытие) -
    /// смешивать их нельзя, у них разные семейства и разные точки вставки.
    /// </summary>
    private bool TryGetSingleStructureKind(
        IReadOnlyCollection<FamilyInstance> instances,
        OpeningsConfig config,
        out StructureKind structureKind) {

        structureKind = default;
        var kinds = instances
            .Select(instance => GetOpeningType(instance, config))
            .ToList();

        if(kinds.Any(kind => kind is null)) {
            return false;
        }

        var structureKinds = kinds
            .Select(kind => kind is OpeningType.WallRectangle or OpeningType.WallRound
                ? StructureKind.Wall
                : StructureKind.Floor)
            .Distinct()
            .ToList();

        if(structureKinds.Count != 1) {
            return false;
        }

        structureKind = structureKinds[0];
        return true;
    }

    private OpeningType? GetOpeningType(FamilyInstance instance, OpeningsConfig config) {
        string familyName = instance.Symbol.FamilyName;
        foreach(OpeningType type in Enum.GetValues(typeof(OpeningType))) {
            if(config.Naming.GetFamilyName(type).Equals(familyName, StringComparison.CurrentCultureIgnoreCase)) {
                return type;
            }
        }

        return null;
    }

    /// <summary>
    /// Собирает id исходных инженерных элементов выбранных заданий из их описаний, без повторов
    /// и без тех, что успели исчезнуть из модели.
    /// </summary>
    private IReadOnlyCollection<ElementId> GetSourceMepIds(
        Document doc,
        IReadOnlyCollection<FamilyInstance> instances,
        OpeningsConfig config) {

        return instances
            .SelectMany(instance => OpeningSourceDescription.ParseMepElementIds(
                instance.LookupParameter(config.Naming.DescriptionParam)?.AsString()))
            .Distinct()
            .Select(id => new ElementId(id))
            .Where(id => doc.GetElement(id) is not null)
            .ToList();
    }

    /// <summary>
    /// Ограничивает интерактивный выбор экземплярами семейств заданий на отверстия.
    /// </summary>
    private class OpeningTaskSelectionFilter : ISelectionFilter {
        private readonly HashSet<string> _familyNames;

        public OpeningTaskSelectionFilter(HashSet<string> familyNames) {
            _familyNames = familyNames;
        }

        public bool AllowElement(Element elem) {
            return elem is FamilyInstance instance
                && instance.Symbol is not null
                && _familyNames.Contains(instance.Symbol.FamilyName);
        }

        public bool AllowReference(Reference reference, XYZ position) {
            return false;
        }
    }
}
