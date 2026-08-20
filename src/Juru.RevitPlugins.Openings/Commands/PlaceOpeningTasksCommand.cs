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

        if(!TryGetMepDiscipline(doc, out var discipline, out string namingError)) {
            TaskDialog.Show("Задания на отверстия", namingError);
            return Result.Cancelled;
        }

        var config = OpeningsConfig.GetOpeningsConfig();
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
        var placement = new OpeningTaskPlacer(doc, config).Place(validation.Accepted);

        TaskDialog.Show("Задания на отверстия",
            BuildReport(discipline, structureProviders, linksProvider, mepProvider, intersections, validation, placement));

        if(placement.Placed.Count > 0) {
            uiDocument.Selection.SetElementIds(placement.Placed.Select(item => item.Id).ToList());
        }
        return Result.Succeeded;
    }

    /// <summary>
    /// Проверяет, что команда запущена в файле инженерных систем: задания выдаёт только он.
    /// </summary>
    private bool TryGetMepDiscipline(Document doc, out Discipline discipline, out string error) {
        discipline = default;
        error = null;

        if(!RevitFileNameInfo.TryParse(doc.PathName, out var fileInfo)) {
            error = "Имя файла не соответствует стандарту наименования "
                + "(Проект-Объект-Дисциплина-vNN), поэтому дисциплину определить нельзя.";
            return false;
        }

        if(!fileInfo.Discipline.IssuesOpeningTasks()) {
            error = $"Дисциплина файла - {fileInfo.Discipline}. "
                + "Задания на отверстия выдаются из файлов WSS, ELE и HVC.";
            return false;
        }

        discipline = fileInfo.Discipline;
        return true;
    }

    private string BuildReport(
        Discipline discipline,
        IReadOnlyCollection<StructureElementsProvider> structureProviders,
        StructureLinksProvider linksProvider,
        MepElementsProvider mepProvider,
        IReadOnlyCollection<MepStructureIntersection> intersections,
        IntersectionValidator.ValidationResult validation,
        PlacementResult placement) {

        var report = new StringBuilder();
        report.AppendLine($"Дисциплина файла: {discipline}");
        report.AppendLine("Связи с конструкциями: "
            + string.Join(", ", structureProviders.Select(item => $"{item.LinkName} ({item.Discipline})")));

        string unrecognized = GetUnrecognizedLinksText(linksProvider);
        if(unrecognized.Length > 0) {
            report.AppendLine(unrecognized);
        }

        report.AppendLine();
        report.AppendLine($"Инженерных элементов проверено: {mepProvider.GetCandidates().Count}");
        report.AppendLine($"Пересечений найдено: {intersections.Count}");
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
