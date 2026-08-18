using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

using Juru.RevitPlugins.Schedules.Models;
using Juru.RevitPlugins.Schedules.Services;

using Nice3point.Revit.Toolkit.External;

namespace Juru.RevitPlugins.Schedules.Commands;

/// <summary>
///     Разбирает все спецификации модели и сохраняет отчёт с заготовкой глоссария. Модель не изменяется.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.ReadOnly)]
public sealed class AuditSchedulesCommand : ExternalCommand
{
    public override void Execute()
    {
        var report = new ScheduleAuditService(Application.ActiveUIDocument.Document).Audit();
        var glossarySeed = GlossarySeedBuilder.Build(report);
        var reportDirectory = ScheduleAuditReportWriter.Write(report, glossarySeed);

        ShowResult(report, glossarySeed, reportDirectory);
    }

    private static void ShowResult(
        ScheduleAuditReport report,
        IReadOnlyList<GlossarySeedEntry> glossarySeed,
        string reportDirectory)
    {
        var fieldsToTranslate = report.Schedules
            .SelectMany(schedule => schedule.Fields)
            .Count(field => field.RequiresTranslation);

        var dialog = new TaskDialog("Разбор спецификаций")
        {
            MainInstruction = $"Спецификаций: {report.Schedules.Count}",
            MainContent = $"""
                           Колонок под перевод: {fieldsToTranslate}
                           Строк в глоссарии: {glossarySeed.Count}

                           {reportDirectory}
                           """,
            CommonButtons = TaskDialogCommonButtons.Close
        };

        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Открыть папку отчёта");

        if (dialog.Show() is TaskDialogResult.CommandLink1)
        {
            Process.Start("explorer.exe", $"\"{reportDirectory}\"");
        }
    }
}
