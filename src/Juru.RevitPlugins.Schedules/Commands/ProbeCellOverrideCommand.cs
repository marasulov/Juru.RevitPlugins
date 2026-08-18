using System.Diagnostics;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

using Juru.RevitPlugins.Schedules.Services;

using Nice3point.Revit.Toolkit.External;

namespace Juru.RevitPlugins.Schedules.Commands;

/// <summary>
///     Проверяет, ложится ли перевод поверх ячейки копии спецификации или уходит в параметр элемента.
///     Работает в транзакции с откатом: модель остаётся нетронутой.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public sealed class ProbeCellOverrideCommand : ExternalCommand
{
    public override void Execute()
    {
        var uiDocument = Application.ActiveUIDocument;

        var report = new CellOverrideProbe(uiDocument.Document).Run(uiDocument.ActiveView);
        var reportPath = CellOverrideProbeWriter.Write(report);

        var dialog = new TaskDialog("Зонд переопределения ячеек")
        {
            MainInstruction = CellOverrideProbeWriter.BuildVerdict(report),
            MainContent = $"""
                           Спецификация: {report.ScheduleName}
                           Проверено ячеек: {report.Attempts.Count}

                           {reportPath}
                           """,
            CommonButtons = TaskDialogCommonButtons.Close
        };

        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Открыть отчёт");

        if (dialog.Show() is TaskDialogResult.CommandLink1)
        {
            Process.Start("explorer.exe", $"\"{reportPath}\"");
        }
    }
}
