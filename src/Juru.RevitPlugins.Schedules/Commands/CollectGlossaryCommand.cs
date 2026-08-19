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
///     Собирает глоссарий по спецификациям на листах, переводит пустые строки машинным переводом и вливает
///     результат в настроенный глоссарий. Уже сделанные переводы сохраняются, строки других моделей из
///     общего файла не теряются. Модель не изменяется.
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
        var glossaryPath = config.ResolveGlossaryPath();
        var existing = GlossaryFile.ReadEntries(glossaryPath);
        var baseTerms = ReadBaseTerms(config.ResolveBaseGlossaryPath());

        var reportDirectory = ReportLocation.Create("Glossary", document.Title, DateTime.Now);
        var collected = new GlossaryCollector(uiDocument).Collect(reportDirectory, existing, baseTerms);

        if (collected.Entries.Count is 0)
        {
            TaskDialog.Show(
                "Сбор глоссария",
                $"На листах {collected.ScheduleCount} спецификаций, русского текста в них не найдено.");

            return;
        }

        var translated = Translate(config.Translation, collected.Entries, out var translatorNote);
        var merged = Merge(translated.Entries, existing);

        var runPath = Path.Combine(reportDirectory, "glossary.csv");
        GlossaryFile.Write(merged, runPath);
        GlossaryFile.Write(merged, glossaryPath);

        ShowResult(collected, translated, merged, baseTerms.Count, translatorNote, glossaryPath, runPath);
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
    ///     Переводит строки без перевода. Отсутствие настроенной службы не ошибка: строки просто остаются
    ///     пустыми и попадают в глоссарий как задача.
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
        GlossaryCollectionResult collected,
        GlossaryTranslationResult translated,
        IReadOnlyList<GlossaryEntry> merged,
        int baseTermCount,
        string translatorNote,
        string glossaryPath,
        string runPath)
    {
        var untranslated = merged.Count(entry => entry.Suggestion.Length is 0);
        var machine = merged.Count(entry => entry.Source is TranslationSource.Machine);
        var fromBase = merged.Count(entry => entry.Source is TranslationSource.Base);

        var rejected = translated.RejectedTexts.Count is 0
            ? string.Empty
            : $"\nОтклонено из-за расхождения чисел: {translated.RejectedTexts.Count}. «{translated.RejectedTexts[0]}»";

        var failures = translated.Failures.Count is 0
            ? string.Empty
            : $"\nОшибок перевода: {translated.Failures.Count}. {translated.Failures[0]}";

        var dialog = new TaskDialog("Сбор глоссария")
        {
            MainInstruction = $"Строк в глоссарии: {merged.Count}, без перевода {untranslated}",
            MainContent = $"""
                           Спецификаций на листах: {collected.ScheduleCount}
                           Из базового словаря ({baseTermCount} терминов): {fromBase}
                           Служба перевода: {translatorNote}
                           Переведено сейчас: {translated.TranslatedCount}, машинных всего: {machine}{rejected}{failures}

                           Глоссарий: {glossaryPath}
                           Копия прогона: {runPath}
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
