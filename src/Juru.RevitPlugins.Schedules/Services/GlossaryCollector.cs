using System;
using System.Collections.Generic;
using System.Linq;

using Juru.RevitPlugins.Schedules.Models;

namespace Juru.RevitPlugins.Schedules.Services;

/// <summary>
///     Строит глоссарий по уже прочитанным спецификациям. Ключами становятся те же строки, что уходят в
///     подстановку при отрисовке, иначе перевод не найдётся.
/// </summary>
public static class GlossaryCollector
{
    /// <param name="existing">
    ///     Уже заполненный глоссарий: сделанные переводы переносятся, чтобы не переводить заново.
    /// </param>
    /// <param name="baseTerms">
    ///     Словарь повторяющихся терминов по нормализованному тексту. Проверяется после глоссария проекта,
    ///     поэтому правка в проекте всегда сильнее общего словаря.
    /// </param>
    public static GlossaryCollectionResult Collect(
        IReadOnlyList<ReadSchedule> schedules,
        IReadOnlyDictionary<string, GlossaryTranslation> existing,
        IReadOnlyDictionary<string, string> baseTerms)
    {
        if (schedules is null)
        {
            throw new ArgumentNullException(nameof(schedules));
        }

        var occurrences = new Dictionary<string, int>(StringComparer.CurrentCulture);
        var scheduleNames = new Dictionary<string, SortedSet<string>>(StringComparer.CurrentCulture);
        var failures = new List<string>();

        foreach (var read in schedules)
        {
            if (read.Snapshot is null)
            {
                failures.Add($"{read.Source.Schedule.Name}: {read.Described.Error}");
                continue;
            }

            foreach (var cell in read.Snapshot.Sections.SelectMany(section => section.Cells))
            {
                Add(occurrences, scheduleNames, cell.Text, read.Source.Schedule.Name);
            }
        }

        return new GlossaryCollectionResult
        {
            ScheduleCount = schedules.Count,
            Entries = BuildEntries(occurrences, scheduleNames, existing, baseTerms),
            Failures = failures
        };
    }

    private static void Add(
        Dictionary<string, int> occurrences,
        Dictionary<string, SortedSet<string>> scheduleNames,
        string text,
        string scheduleName)
    {
        var key = text.Trim();

        // Строка уже двуязычная - в модели лежит «исходник — перевод». Ключом идёт исходная
        // половина: иначе в словаре появился бы ключ с русским и английским разом, и с каждым
        // прогоном такие ключи множились бы.
        if (RussianText.TrySplitBilingual(key, out var written, out _))
        {
            key = written;
        }

        if (!RussianText.ContainsCyrillic(key) || RussianText.IsSpacerLabel(key))
        {
            return;
        }

        occurrences.TryGetValue(key, out var count);
        occurrences[key] = count + 1;

        if (!scheduleNames.TryGetValue(key, out var names))
        {
            names = new SortedSet<string>(StringComparer.CurrentCulture);
            scheduleNames[key] = names;
        }

        names.Add(scheduleName);
    }

    private static IReadOnlyList<GlossaryEntry> BuildEntries(
        Dictionary<string, int> occurrences,
        Dictionary<string, SortedSet<string>> scheduleNames,
        IReadOnlyDictionary<string, GlossaryTranslation> existing,
        IReadOnlyDictionary<string, string> baseTerms)
    {
        return occurrences
            .Select(pair => CreateEntry(pair.Key, pair.Value, scheduleNames[pair.Key], existing, baseTerms))
            .OrderBy(entry => entry.Readable, StringComparer.CurrentCulture)
            .ToList();
    }

    private static GlossaryEntry CreateEntry(
        string key,
        int occurrences,
        IEnumerable<string> scheduleNames,
        IReadOnlyDictionary<string, GlossaryTranslation> existing,
        IReadOnlyDictionary<string, string> baseTerms)
    {
        var readable = RussianText.Normalize(key);
        var translation = ResolveTranslation(key, readable, existing, baseTerms);

        return new GlossaryEntry
        {
            Key = key,
            Readable = readable,
            Suggestion = translation.Text,
            Source = translation.Source,
            Occurrences = occurrences,
            Schedules = scheduleNames.ToList()
        };
    }

    /// <summary>
    ///     Заполняет перевод там, где он известен: сделанный ранее перевод, выверенный термин, обозначение
    ///     стандарта, марка изделия или английская половина двуязычной строки. Марки и обозначения в
    ///     переводе остаются собой.
    /// </summary>
    private static GlossaryTranslation ResolveTranslation(
        string key,
        string readable,
        IReadOnlyDictionary<string, GlossaryTranslation> existing,
        IReadOnlyDictionary<string, string> baseTerms)
    {
        if (existing.TryGetValue(key, out var carried))
        {
            return carried;
        }

        if (baseTerms.TryGetValue(readable, out var term))
        {
            return new GlossaryTranslation(term, TranslationSource.Base);
        }

        if (RussianText.IsStandardDesignation(key) || RussianText.IsProductMark(key))
        {
            return new GlossaryTranslation(key, TranslationSource.Derived);
        }

        var english = RussianText.TakeEnglishPart(key);

        return english.Length > 0
            ? new GlossaryTranslation(english, TranslationSource.Derived)
            : new GlossaryTranslation(string.Empty, TranslationSource.None);
    }
}
