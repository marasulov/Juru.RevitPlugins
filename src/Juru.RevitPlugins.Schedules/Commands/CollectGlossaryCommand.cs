using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

using Juru.RevitPlugins.Schedules.Services;

using Nice3point.Revit.Toolkit;
using Nice3point.Revit.Toolkit.External;

namespace Juru.RevitPlugins.Schedules.Commands;

/// <summary>
///     Собирает глоссарий по спецификациям, размещённым на листах, и сохраняет его рядом с уже заполненным,
///     не затирая его. Модель не изменяется.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public sealed class CollectGlossaryCommand : ExternalCommand
{
    public override void Execute()
    {
        var uiDocument = Application.ActiveUIDocument;
        var document = uiDocument.Document;

        using var dialogSuppression = RevitContext.BeginDialogSuppressionScope(TaskDialogResult.Ok);

        var config = SchedulesConfig.Load();
        var existing = Glossary.Load(config.ResolveGlossaryPath());

        var reportDirectory = ReportLocation.Create("Glossary", document.Title, DateTime.Now);
        var result = new GlossaryCollector(uiDocument).Collect(reportDirectory, existing);

        if (result.Entries.Count is 0)
        {
            TaskDialog.Show("Сбор глоссария", $"На листах {result.ScheduleCount} спецификаций, русского текста в них не найдено.");
            return;
        }

        var glossaryPath = Path.Combine(reportDirectory, "glossary.csv");
        GlossaryFile.Write(result.Entries, glossaryPath);

        ShowResult(result, glossaryPath, config.ResolveGlossaryPath());
    }

    private static void ShowResult(
        Models.GlossaryCollectionResult result,
        string glossaryPath,
        string configuredPath)
    {
        var prefilled = result.Entries.Count(entry => entry.Suggestion.Length > 0);
        var failures = result.Failures.Count is 0
            ? string.Empty
            : $"\nНе прочитано спецификаций: {result.Failures.Count}. {result.Failures[0]}";

        var dialog = new TaskDialog("Сбор глоссария")
        {
            MainInstruction = $"Строк на перевод: {result.Entries.Count}",
            MainContent = $"""
                           Спецификаций на листах: {result.ScheduleCount}
                           Перевод предзаполнен: {prefilled}{failures}

                           {glossaryPath}

                           Заполните колонку «Перевод» и положите файл сюда:
                           {configuredPath}
                           """,
            CommonButtons = TaskDialogCommonButtons.Close
        };

        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Открыть глоссарий");

        if (dialog.Show() is TaskDialogResult.CommandLink1)
        {
            Process.Start("explorer.exe", $"\"{glossaryPath}\"");
        }
    }
}
