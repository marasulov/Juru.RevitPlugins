using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

using Juru.RevitPlugins.Schedules.Models;
using Juru.RevitPlugins.Schedules.Services;
using Juru.RevitPlugins.Schedules.Services.Translation;

using Nice3point.Revit.Toolkit;
using Nice3point.Revit.Toolkit.External;

namespace Juru.RevitPlugins.Schedules.Commands;

/// <summary>
///     Делает английский комплект целиком: читает спецификации с листов, пополняет и переводит глоссарий,
///     перерисовывает каждую спецификацию в чертёжный вид и кладёт копию на лист рядом с оригиналом.
///     Спецификации читаются один раз, глоссарий заполняется до отрисовки — иначе копия вышла бы русской.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public sealed class BuildEnglishSchedulesCommand : ExternalCommand
{
    public override void Execute()
    {
        var uiDocument = Application.ActiveUIDocument;
        var document = uiDocument.Document;

        // Проверки значений показывают модальные окна, которые обрывают пакетный прогон.
        using var dialogSuppression = RevitContext.BeginDialogSuppressionScope(TaskDialogResult.Ok);

        var config = SchedulesConfig.Load();
        var glossaryPath = config.ResolveGlossaryPath();
        var runStamp = DateTime.Now;
        var reportDirectory = ReportLocation.Create("EnglishSchedules", document.Title, runStamp);

        var service = new SheetScheduleCopyService(uiDocument);
        var schedules = service.ReadAll(reportDirectory);

        if (schedules.Count is 0)
        {
            TaskDialog.Show("Английские спецификации", "На листах нет ни одной спецификации.");
            return;
        }

        var glossary = BuildGlossary(config, glossaryPath, schedules, reportDirectory, out var glossaryReport);
        var copies = service.DrawAll(schedules, glossary, reportDirectory);

        var copyReportPath = SheetScheduleCopyReportWriter.Write(copies, document.Title, glossary, runStamp);
        var untranslatedPath = UntranslatedWriter.Write(glossary, reportDirectory);

        ShowResult(copies, glossaryReport, copyReportPath, untranslatedPath, glossaryPath);
    }

    /// <summary>
    ///     Пополняет глоссарий строками этой модели, переводит недостающие и сохраняет его, а затем
    ///     перечитывает: подстановка при отрисовке идёт по тому же файлу, что увидит пользователь.
    /// </summary>
    private static Glossary BuildGlossary(
        SchedulesConfig config,
        string glossaryPath,
        IReadOnlyList<ReadSchedule> schedules,
        string reportDirectory,
        out GlossaryReport report)
    {
        var existing = GlossaryFile.ReadEntries(glossaryPath);
        var baseTerms = ReadBaseTerms(config.ResolveBaseGlossaryPath());

        var collected = GlossaryCollector.Collect(schedules, existing, baseTerms);
        var translated = Translate(config.Translation, collected.Entries, out var translatorNote);
        var merged = Merge(translated.Entries, existing);

        GlossaryFile.Write(merged, glossaryPath);
        GlossaryFile.Write(merged, Path.Combine(reportDirectory, "glossary.csv"));

        report = new GlossaryReport
        {
            TotalCount = merged.Count,
            BaseTermCount = baseTerms.Count,
            FromBaseCount = merged.Count(entry => entry.Source is TranslationSource.Base),
            MachineCount = merged.Count(entry => entry.Source is TranslationSource.Machine),
            TranslatedNowCount = translated.TranslatedCount,
            UntranslatedCount = merged.Count(entry => entry.Suggestion.Length is 0),
            RejectedCount = translated.RejectedTexts.Count,
            TranslatorNote = translatorNote,
            Failures = translated.Failures
        };

        return Glossary.Load(glossaryPath);
    }

    /// <summary>
    ///     Базовый словарь ищется по нормализованному тексту: термин переносится между моделями, даже если
    ///     переносы в заголовке расставлены по-разному.
    /// </summary>
    private static IReadOnlyDictionary<string, string> ReadBaseTerms(string path)
    {
        var terms = new Dictionary<string, string>(StringComparer.CurrentCulture);

        foreach (var pair in GlossaryFile.ReadEntries(path))
        {
            terms[RussianText.Normalize(pair.Key)] = pair.Value.Text;
        }

        return terms;
    }

    /// <summary>
    ///     Переводит строки без перевода. Отсутствие настроенной службы не ошибка: строки остаются пустыми
    ///     и попадают в отчёт как задача.
    /// </summary>
    private static GlossaryTranslationResult Translate(
        TranslationSettings settings,
        IReadOnlyList<GlossaryEntry> entries,
        out string note)
    {
        var translator = TranslatorFactory.TryCreate(settings, out var reason);

        if (translator is null)
        {
            note = reason ?? "не настроена";

            return new GlossaryTranslationResult
            {
                Entries = entries,
                TranslatedCount = 0,
                RejectedTexts = [],
                Failures = []
            };
        }

        note = translator.Name;

        // Внешний командный интерфейс Revit синхронный, поэтому запросы выполняются на отдельном потоке.
        return Task.Run(() => new GlossaryTranslator(translator, settings.BatchSize)
                .TranslateAsync(entries, CancellationToken.None))
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>
    ///     Добавляет строки, которых нет в текущей модели: общий глоссарий накапливает переводы по всем
    ///     моделям, и перезапись только собранным потеряла бы остальные.
    /// </summary>
    private static IReadOnlyList<GlossaryEntry> Merge(
        IReadOnlyList<GlossaryEntry> entries,
        IReadOnlyDictionary<string, GlossaryTranslation> existing)
    {
        var collectedKeys = entries.Select(entry => entry.Key).ToHashSet(StringComparer.CurrentCulture);

        var carried = existing
            .Where(pair => !collectedKeys.Contains(pair.Key))
            .Select(pair => new GlossaryEntry
            {
                Key = pair.Key,
                Readable = RussianText.Normalize(pair.Key),
                Suggestion = pair.Value.Text,
                Source = pair.Value.Source,
                Occurrences = 0,
                Schedules = []
            });

        return entries
            .Concat(carried)
            .OrderBy(entry => entry.Readable, StringComparer.CurrentCulture)
            .ToList();
    }

    private static void ShowResult(
        IReadOnlyList<SheetScheduleCopy> copies,
        GlossaryReport glossary,
        string copyReportPath,
        string? untranslatedPath,
        string glossaryPath)
    {
        var failed = copies.Count(copy => copy.Error is not null);

        var rejected = glossary.RejectedCount is 0
            ? string.Empty
            : $", отклонено по числам {glossary.RejectedCount}";

        var failures = glossary.Failures.Count is 0
            ? string.Empty
            : $"\nОшибок перевода: {glossary.Failures.Count}. {glossary.Failures[0]}";

        var untranslated = untranslatedPath is null
            ? "Непереведённых строк нет."
            : $"Непереведённые строки: {untranslatedPath}";

        var dialog = new TaskDialog("Английские спецификации")
        {
            MainInstruction = $"Готово {copies.Count - failed} из {copies.Count}",
            MainContent = $"""
                           Глоссарий: {glossary.TotalCount} строк, без перевода {glossary.UntranslatedCount}
                           Из базового словаря: {glossary.FromBaseCount}, машинных: {glossary.MachineCount}
                           Служба перевода: {glossary.TranslatorNote}, переведено сейчас {glossary.TranslatedNowCount}{rejected}{failures}

                           Примечаний: {copies.Sum(copy => copy.TextNoteCount)}, линий: {copies.Sum(copy => copy.LineCount)}
                           Ошибок копирования: {failed}

                           {copyReportPath}
                           {untranslated}
                           Глоссарий: {glossaryPath}
                           """,
            CommonButtons = TaskDialogCommonButtons.Close
        };

        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Открыть отчёт");

        if (dialog.Show() is TaskDialogResult.CommandLink1)
        {
            Process.Start("explorer.exe", $"\"{copyReportPath}\"");
        }
    }
}
