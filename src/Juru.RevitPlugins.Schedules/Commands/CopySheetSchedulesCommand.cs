using System;
using System.Diagnostics;
using System.Linq;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

using Juru.RevitPlugins.Schedules.Services;

using Nice3point.Revit.Toolkit;
using Nice3point.Revit.Toolkit.External;

namespace Juru.RevitPlugins.Schedules.Commands;

/// <summary>
///     Копирует в чертёжные виды все спецификации, размещённые на листах, подставляя перевод из глоссария.
///     Копии статические: при изменении модели их создают заново.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public sealed class CopySheetSchedulesCommand : ExternalCommand
{
    public override void Execute()
    {
        var uiDocument = Application.ActiveUIDocument;
        var document = uiDocument.Document;
        var config = SchedulesConfig.Load();
        var glossary = Glossary.Load(config.ResolveGlossaryPath());

        // Проверки значений показывают модальные окна, которые обрывают пакетный прогон.
        using var dialogSuppression = RevitContext.BeginDialogSuppressionScope(TaskDialogResult.Ok);

        var runStamp = DateTime.Now;
        var dumpDirectory = ReportLocation.Create("SheetScheduleCopy", document.Title, runStamp);

        var copies = new SheetScheduleCopyService(uiDocument).CopyAll(glossary, dumpDirectory);

        if (copies.Count is 0)
        {
            TaskDialog.Show("Копии спецификаций", "На листах нет ни одной спецификации.");
            return;
        }

        var reportPath = SheetScheduleCopyReportWriter.Write(copies, document.Title, glossary, runStamp);
        var untranslatedPath = UntranslatedWriter.Write(glossary, dumpDirectory);

        var failed = copies.Count(copy => copy.Error is not null);
        var skippedCells = copies.Sum(copy => copy.SkippedCellCount);

        var dialog = new TaskDialog("Копии спецификаций")
        {
            MainInstruction = $"Скопировано {copies.Count - failed} из {copies.Count}",
            MainContent = $"""
                           Примечаний: {copies.Sum(copy => copy.TextNoteCount)}, линий: {copies.Sum(copy => copy.LineCount)}
                           Ошибок: {failed}, не размещено ячеек: {skippedCells}

                           {reportPath}
                           {(untranslatedPath is null ? "Непереведённых строк нет." : $"Непереведённые строки: {untranslatedPath}")}
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
