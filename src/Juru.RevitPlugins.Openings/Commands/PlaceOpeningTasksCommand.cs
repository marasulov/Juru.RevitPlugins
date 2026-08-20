using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Juru.RevitPlugins.Core.FileNaming;
using Juru.RevitPlugins.Openings.Models.Configs;
using Juru.RevitPlugins.Openings.Models.Intersections;
using Juru.RevitPlugins.Openings.Models.Placement;

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
        if(!TryGetMepDiscipline(doc, config, out var discipline, out bool byStandard, out string namingError)) {
            TaskDialog.Show("Задания на отверстия", namingError);
            return Result.Cancelled;
        }

        if(config.GetSelectedCategories().Count == 0) {
            TaskDialog.Show("Задания на отверстия",
                "В настройках не выбран ни один вид инженерных элементов.");
            return Result.Cancelled;
        }

        var linksProvider = new StructureLinksProvider(doc, config);
        var structureProviders = linksProvider.GetProviders();
        if(structureProviders.Count == 0) {
            TaskDialog.Show("Задания на отверстия",
                "Не найдено ни одной связи с конструкциями.\n\n"
                + "Связи отбираются по дисциплине в имени файла: ARC или STR. "
                + GetUnrecognizedLinksText(linksProvider));
            return Result.Cancelled;
        }

        var mepProvider = new MepElementsProvider(doc, config);
        if(mepProvider.GetCandidates().Count == 0) {
            TaskDialog.Show("Задания на отверстия",
                "В файле нет инженерных элементов, подходящих под настройки: "
                + "проверьте выбранные виды и диапазоны габаритов.");
            return Result.Cancelled;
        }

        var intersections = new SolidIntersectionFinder(mepProvider).Find(structureProviders);
        var validation = new IntersectionValidator().Validate(intersections);

        var calculator = new OpeningSizeCalculator();
        var planned = validation.Accepted
            .Select(calculator.Calculate)
            .Where(opening => opening is not null)
            .ToArray();
        var merged = new OpeningMerger(config).Merge(planned);
        var placement = new OpeningTaskPlacer(doc, config).Place(merged);

        TaskDialog.Show("Задания на отверстия",
            BuildReport(discipline, byStandard, structureProviders, linksProvider,
                mepProvider, intersections, validation, merged, placement));

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
        out Discipline discipline,
        out bool byStandard,
        out string error) {

        error = null;
        if(!new DisciplineResolver(config).TryResolve(doc.PathName, out discipline, out byStandard)) {
            error = "Дисциплину файла определить не удалось: имя не соответствует стандарту "
                + "(Проект-Объект-Дисциплина-vNN) и не подходит ни под одно правило из настроек.";
            return false;
        }

        if(!discipline.IssuesOpeningTasks()) {
            error = "Дисциплина файла - " + discipline
                + ". Задания на отверстия выдаются из файлов WSS, ELE и HVC.";
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
        PlacementResult placement) {

        var report = new StringBuilder();
        report.AppendLine("Дисциплина файла: " + discipline
            + (byStandard ? string.Empty : " (по правилу из настроек, имя не по стандарту)"));
        report.AppendLine("Связи с конструкциями: "
            + string.Join(", ", structureProviders.Select(item => $"{item.LinkName} ({item.Discipline})")));

        string unrecognized = GetUnrecognizedLinksText(linksProvider);
        if(unrecognized.Length > 0) {
            report.AppendLine(unrecognized);
        }

        report.AppendLine();
        report.AppendLine($"Инженерных элементов проверено: {mepProvider.GetCandidates().Count}");
        report.AppendLine($"Пересечений найдено: {intersections.Count}");
        int unitedCount = merged.Count(item => item.IsUnited);
        if(unitedCount > 0) {
            int mergedSources = merged.Where(item => item.IsUnited).Sum(item => item.Sources.Count);
            report.AppendLine($"Объединённых заданий: {unitedCount} (склеено проходов: {mergedSources})");
        }
        report.AppendLine($"Заданий размещено: {placement.Placed.Count}");
        if(placement.SkippedAsDuplicate > 0) {
            report.AppendLine($"Пропущено, задание уже стоит: {placement.SkippedAsDuplicate}");
        }

        if(validation.Rejected.Count > 0) {
            report.AppendLine();
            report.AppendLine($"Отклонено пересечений: {validation.Rejected.Count}");
            foreach(var reason in validation.GetReasonSummary()) {
                report.AppendLine($"  {reason.Value} - {reason.Key}");
            }
        }

        if(placement.Failed.Count > 0) {
            report.AppendLine();
            report.AppendLine($"Не удалось разместить: {placement.Failed.Count}");
            foreach(var reason in placement.GetFailureSummary()) {
                report.AppendLine($"  {reason.Value} - {reason.Key}");
            }
        }

        if(placement.MissingParameters.Count > 0) {
            report.AppendLine();
            report.AppendLine("Параметров нет в семействах, значения записать некуда:");
            report.AppendLine("  " + string.Join(", ", placement.MissingParameters));
        }

        return report.ToString();
    }

    private string GetUnrecognizedLinksText(StructureLinksProvider linksProvider) {
        var unrecognized = linksProvider.GetUnrecognizedLinkNames();
        return unrecognized.Count == 0
            ? string.Empty
            : "Связи с именем не по стандарту (в поиске не участвуют): " + string.Join(", ", unrecognized);
    }
}
