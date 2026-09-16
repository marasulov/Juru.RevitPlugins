using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Juru.RevitPlugins.Schedules.Models;
using Juru.RevitPlugins.Schedules.Services;

using Nice3point.Revit.Toolkit;
using Nice3point.Revit.Toolkit.External;

namespace Juru.RevitPlugins.Schedules.Commands;

/// <summary>
///     Перерисовывает активную спецификацию в чертёжный вид, подставляя перевод из глоссария. Результат —
///     статическая копия: при изменении модели её надо создавать заново.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public sealed class CopyScheduleToDraftingViewCommand : ExternalCommand
{
    public override void Execute()
    {
        var uiDocument = Application.ActiveUIDocument;
        var document = uiDocument.Document;

        if (uiDocument.ActiveView is not ViewSchedule schedule)
        {
            TaskDialog.Show("Копия спецификации", "Откройте спецификацию и повторите команду.");
            return;
        }

        var config = SchedulesConfig.Load();
        var glossary = Glossary.Load(config.ResolveGlossaryPath());

        // Проверка значений параметров показывает модальное окно, которое обрывает отрисовку на середине.
        using var dialogSuppression = RevitContext.BeginDialogSuppressionScope(TaskDialogResult.Ok);

        var exportedText = new ScheduleTextExporter()
            .TryExport(schedule, System.IO.Path.Combine(System.IO.Path.GetTempPath(), "JuruSchedules"), out _);

        using var transaction = new Transaction(document, $"Английская копия: {schedule.Name}");
        transaction.Start();

        var snapshot = new ScheduleTableReader().Read(schedule, exportedText);
        var writer = new DraftingTableWriter(document);
        var result = writer.Draw(snapshot, glossary);

        transaction.Commit();

        var diagnosticsPath = ScheduleTableDiagnosticsWriter.Write(snapshot, writer.AppliedViewScale, DateTime.Now);

        ShowResult(result, glossary, writer.AppliedViewScale, diagnosticsPath);
    }

    private static void ShowResult(
        DraftingTableResult result,
        Glossary glossary,
        int appliedViewScale,
        string diagnosticsPath)
    {
        var glossaryState = glossary.SourcePath is null
            ? $"Глоссарий не найден, текст оставлен русским.\nОжидается: {Glossary.DefaultPath}"
            : $"Глоссарий: {glossary.TranslationCount} строк.";

        var missing = glossary.Misses.Count is 0
            ? "Непереведённых строк нет."
            : $"Без перевода: {glossary.Misses.Count} строк, включая «{FirstMisses(glossary)}».";

        var skipped = result.SkippedCellCount is 0
            ? string.Empty
            : $"\nНе размещено ячеек: {result.SkippedCellCount}. {result.FirstSkipReason}";

        var textSize = result.TextSizeNote is null ? string.Empty : $"\n{result.TextSizeNote}";

        var dialog = new TaskDialog("Копия спецификации")
        {
            MainInstruction = result.ViewName,
            MainContent = $"""
                           Масштаб вида: 1:{appliedViewScale}
                           Линий: {result.LineCount}, текстовых примечаний: {result.TextNoteCount}{skipped}{textSize}

                           {glossaryState}
                           {missing}

                           Размеры таблицы: {diagnosticsPath}
                           """,
            CommonButtons = TaskDialogCommonButtons.Close
        };

        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Открыть размеры таблицы");

        if (dialog.Show() is TaskDialogResult.CommandLink1)
        {
            Process.Start("explorer.exe", $"\"{diagnosticsPath}\"");
        }
    }

    private static string FirstMisses(Glossary glossary)
    {
        return string.Join("», «", glossary.Misses.Keys.Take(3).Select(Shorten));
    }

    private static string Shorten(string text)
    {
        return text.Length <= 40 ? text : $"{text[..40]}…";
    }
}
