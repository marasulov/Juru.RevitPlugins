using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Juru.RevitPlugins.Core.FileNaming;
using Juru.RevitPlugins.Core.Localization;
using Juru.RevitPlugins.Openings.Models.Configs;
using Juru.RevitPlugins.Openings.Models.Intersections;
using Juru.RevitPlugins.Openings.Models.Placement;
using Juru.RevitPlugins.Openings.Services;

namespace Juru.RevitPlugins.Openings.Commands;

/// <summary>
/// Выдаёт задания на отверстия в файле инженерных систем: находит пересечения с конструкциями
/// из связей, отсеивает ложные и размещает задания.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class PlaceOpeningTasksCommand : IExternalCommand {
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) {
        var uiDocument = commandData.Application.ActiveUIDocument;
        var doc = uiDocument.Document;

        var config = OpeningsConfig.GetOpeningsConfig();
        var culture = PluginCulture.Resolve(config, commandData.Application.Application.Language);
        var localization = PluginLocalization.Load(typeof(PlaceOpeningTasksCommand), culture);

        if(!TryGetMepDiscipline(doc, config, localization, out var discipline, out bool byStandard, out string namingError)) {
            TaskDialog.Show(localization.GetString("Progress.WindowTitle"), namingError);
            return Result.Cancelled;
        }

        if(config.GetSelectedCategories().Count == 0) {
            TaskDialog.Show(
                localization.GetString("Progress.WindowTitle"),
                localization.GetString("Placement.NoCategoriesSelected"));
            return Result.Cancelled;
        }

        var linksProvider = new StructureLinksProvider(doc, config);
        var structureProviders = linksProvider.GetProviders();
        if(structureProviders.Count == 0) {
            TaskDialog.Show(
                localization.GetString("Progress.WindowTitle"),
                localization.GetString("Placement.NoLinksFormat", GetUnrecognizedLinksText(linksProvider, localization)));
            return Result.Cancelled;
        }

        var mepProvider = new MepElementsProvider(doc, config);
        if(mepProvider.GetCandidates().Count == 0) {
            TaskDialog.Show(
                localization.GetString("Progress.WindowTitle"),
                localization.GetString("Placement.NoMepElements"));
            return Result.Cancelled;
        }

        // Раньше, чем расстановка построит свой индекс уже стоящих заданий - иначе она сверялась бы
        // с тем, что вот-вот удалится.
        int orphansRemoved = new OrphanedTaskCleaner().Remove(doc, config);

        IReadOnlyCollection<MepStructureIntersection> intersections;
        IntersectionValidator.ValidationResult validation;
        IReadOnlyCollection<PlannedOpening> merged;
        PlacementResult placement;

        using(var progress = new OperationProgress(commandData.Application.MainWindowHandle, localization)) {
            try {
                int structureCount = structureProviders.Sum(item => item.GetElements().Count);
                progress.StartStage(localization.GetString("Placement.Stage.FindIntersections"), structureCount);
                intersections = new SolidIntersectionFinder(mepProvider)
                    .Find(structureProviders, progress, progress.Token);

                progress.StartStage(localization.GetString("Placement.Stage.CalculateSizes"), intersections.Count);
                validation = new IntersectionValidator(config).Validate(intersections);

                var calculator = new OpeningSizeCalculator();
                var planned = new List<PlannedOpening>();
                int calculated = 0;
                foreach(var accepted in validation.Accepted) {
                    progress.Token.ThrowIfCancellationRequested();
                    progress.Report(++calculated);
                    var opening = calculator.Calculate(accepted);
                    if(opening is not null) {
                        planned.Add(opening);
                    }
                }

                progress.StartStage(localization.GetString("Placement.Stage.PlaceTasks"), planned.Count);
                merged = new OpeningMerger(config).Merge(planned);
                placement = new OpeningTaskPlacer(doc, config).Place(merged);
            } catch(OperationCanceledException) {
                TaskDialog.Show(
                    localization.GetString("Progress.WindowTitle"), localization.GetString("Placement.Cancelled"));
                return Result.Cancelled;
            }
        }

        TaskDialog.Show(
            localization.GetString("Progress.WindowTitle"),
            BuildReport(discipline, byStandard, structureProviders, linksProvider,
                mepProvider, intersections, validation, merged, placement, orphansRemoved, localization));

        if(placement.Placed.Count > 0) {
            uiDocument.Selection.SetElementIds(placement.Placed.Select(item => item.Id).ToList());
        }
        return Result.Succeeded;
    }

    /// <summary>
    /// Проверяет, что команда запущена в файле инженерных систем: задания выдаёт только он.
    /// </summary>
    private bool TryGetMepDiscipline(
        Document doc,
        OpeningsConfig config,
        PluginLocalization localization,
        out Discipline discipline,
        out bool byStandard,
        out string error) {

        error = null;
        if(!new DisciplineResolver(config).TryResolve(doc.PathName, out discipline, out byStandard)) {
            error = localization.GetString("Navigator.WrongFileNameError");
            return false;
        }

        if(!discipline.IssuesOpeningTasks()) {
            error = localization.GetString("Placement.WrongDisciplineErrorFormat", discipline);
            return false;
        }

        return true;
    }

    private string BuildReport(
        Discipline discipline,
        bool byStandard,
        IReadOnlyCollection<StructureElementsProvider> structureProviders,
        StructureLinksProvider linksProvider,
        MepElementsProvider mepProvider,
        IReadOnlyCollection<MepStructureIntersection> intersections,
        IntersectionValidator.ValidationResult validation,
        IReadOnlyCollection<PlannedOpening> merged,
        PlacementResult placement,
        int orphansRemoved,
        PluginLocalization localization) {

        var report = new StringBuilder();
        report.AppendLine(localization.GetString(
            "Report.DisciplineFormat", discipline,
            byStandard ? string.Empty : localization.GetString("Report.DisciplineNotByStandard")));
        if(orphansRemoved > 0) {
            report.AppendLine(localization.GetString("Report.OrphansRemovedFormat", orphansRemoved));
        }
        report.AppendLine(localization.GetString("Report.LinksFormat",
            string.Join(", ", structureProviders.Select(item => $"{item.LinkName} ({item.Discipline})"))));

        string unrecognized = GetUnrecognizedLinksText(linksProvider, localization);
        if(unrecognized.Length > 0) {
            report.AppendLine(unrecognized);
        }

        report.AppendLine();
        report.AppendLine(localization.GetString("Report.MepCheckedFormat", mepProvider.GetCandidates().Count));
        report.AppendLine(localization.GetString("Report.IntersectionsFoundFormat", intersections.Count));
        int unitedCount = merged.Count(item => item.IsUnited);
        if(unitedCount > 0) {
            int mergedSources = merged.Where(item => item.IsUnited).Sum(item => item.Sources.Count);
            report.AppendLine(localization.GetString("Report.UnitedFormat", unitedCount, mergedSources));
        }
        report.AppendLine(localization.GetString("Report.PlacedFormat", placement.Placed.Count));
        if(placement.Updated > 0) {
            report.AppendLine(localization.GetString("Report.MovedFormat", placement.Updated));
        }
        if(placement.SkippedAsDuplicate > 0) {
            report.AppendLine(localization.GetString("Report.SkippedDuplicateFormat", placement.SkippedAsDuplicate));
        }

        if(validation.Rejected.Count > 0) {
            report.AppendLine();
            report.AppendLine(localization.GetString("Report.RejectedFormat", validation.Rejected.Count));
            foreach(var reason in validation.GetReasonSummary()) {
                report.AppendLine(localization.GetString("Report.ReasonFormat", reason.Value, reason.Key));
            }
        }

        if(placement.Failed.Count > 0) {
            report.AppendLine();
            report.AppendLine(localization.GetString("Report.FailedFormat", placement.Failed.Count));
            foreach(var reason in placement.GetFailureSummary()) {
                report.AppendLine(localization.GetString("Report.ReasonFormat", reason.Value, reason.Key));
            }
        }

        if(placement.LoadedFamilies.Count > 0) {
            report.AppendLine();
            report.AppendLine(localization.GetString(
                "Report.LoadedFamiliesFormat", string.Join(", ", placement.LoadedFamilies)));
        }

        if(placement.UpdatedFamilies.Count > 0) {
            report.AppendLine();
            report.AppendLine(localization.GetString(
                "Report.UpdatedFamiliesFormat", string.Join(", ", placement.UpdatedFamilies)));
        }

        if(placement.MissingParameters.Count > 0) {
            report.AppendLine();
            report.AppendLine(localization.GetString("Report.MissingParametersHeader"));
            report.AppendLine(localization.GetString(
                "Report.MissingParametersFormat", string.Join(", ", placement.MissingParameters)));
        }

        return report.ToString();
    }

    private string GetUnrecognizedLinksText(StructureLinksProvider linksProvider, PluginLocalization localization) {
        var unrecognized = linksProvider.GetUnrecognizedLinkNames();
        return unrecognized.Count == 0
            ? string.Empty
            : localization.GetString("Placement.UnrecognizedLinksFormat", string.Join(", ", unrecognized));
    }
}
