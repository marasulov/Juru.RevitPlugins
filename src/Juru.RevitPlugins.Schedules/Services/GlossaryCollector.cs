using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Juru.RevitPlugins.Schedules.Models;

using RevitException = Autodesk.Revit.Exceptions.ApplicationException;

namespace Juru.RevitPlugins.Schedules.Services;

/// <summary>
///     Собирает глоссарий по листовым спецификациям тем же путём, которым потом подставляется перевод:
///     ключи берутся из снимка таблицы, а не из параметров элементов, иначе они не совпадут при подстановке.
///     Модель не изменяется.
/// </summary>
public sealed class GlossaryCollector(UIDocument uiDocument)
{
    private readonly Document document = uiDocument.Document;

    /// <param name="existing">
    ///     Уже заполненный глоссарий: сделанные переводы переносятся в новый файл, чтобы не переводить заново.
    /// </param>
    public GlossaryCollectionResult Collect(
        string exportDirectory,
        IReadOnlyDictionary<string, GlossaryTranslation> existing)
    {
        var originalView = uiDocument.ActiveView;
        var occurrences = new Dictionary<string, int>(StringComparer.CurrentCulture);
        var schedules = new Dictionary<string, SortedSet<string>>(StringComparer.CurrentCulture);
        var failures = new List<string>();

        var found = new SheetScheduleFinder(document).Find();

        foreach (var entry in found)
        {
            try
            {
                CollectSchedule(entry.Schedule, exportDirectory, occurrences, schedules);
            }
            catch (RevitException exception)
            {
                failures.Add($"{entry.Schedule.Name}: {exception.Message}");
            }
        }

        TryActivate(originalView);

        return new GlossaryCollectionResult
        {
            ScheduleCount = found.Count,
            Entries = BuildEntries(occurrences, schedules, existing),
            Failures = failures
        };
    }

    private void CollectSchedule(
        ViewSchedule schedule,
        string exportDirectory,
        Dictionary<string, int> occurrences,
        Dictionary<string, SortedSet<string>> schedules)
    {
        // Экспорт пишет файл на диск, а смена активного вида недопустима внутри транзакции.
        var exportedText = new ScheduleTextExporter()
            .TryExport(schedule, Path.Combine(exportDirectory, "export"), out _);

        TryActivate(schedule);

        using var transaction = new Transaction(document, $"Сбор глоссария: {schedule.Name}");
        transaction.Start();

        var snapshot = new ScheduleTableReader(document).Read(schedule, exportedText);

        transaction.RollBack();

        foreach (var cell in snapshot.Sections.SelectMany(section => section.Cells))
        {
            Add(occurrences, schedules, cell.Text, schedule.Name);
        }
    }

    private static void Add(
        Dictionary<string, int> occurrences,
        Dictionary<string, SortedSet<string>> schedules,
        string text,
        string scheduleName)
    {
        var key = text.Trim();

        if (!RussianText.ContainsCyrillic(key) || RussianText.IsSpacerLabel(key))
        {
            return;
        }

        occurrences.TryGetValue(key, out var count);
        occurrences[key] = count + 1;

        if (!schedules.TryGetValue(key, out var names))
        {
            names = new SortedSet<string>(StringComparer.CurrentCulture);
            schedules[key] = names;
        }

        names.Add(scheduleName);
    }

    private static IReadOnlyList<GlossaryEntry> BuildEntries(
        Dictionary<string, int> occurrences,
        Dictionary<string, SortedSet<string>> schedules,
        IReadOnlyDictionary<string, GlossaryTranslation> existing)
    {
        return occurrences
            .Select(pair => CreateEntry(pair.Key, pair.Value, schedules[pair.Key], existing))
            .OrderBy(entry => entry.Readable, StringComparer.CurrentCulture)
            .ToList();
    }

    /// <summary>
    ///     Заполняет перевод там, где он известен: сделанный ранее перевод, английская половина двуязычной
    ///     строки, обозначение стандарта или марка изделия. Марки и обозначения в переводе остаются собой.
    /// </summary>
    private static GlossaryEntry CreateEntry(
        string key,
        int occurrences,
        IEnumerable<string> scheduleNames,
        IReadOnlyDictionary<string, GlossaryTranslation> existing)
    {
        var translation = ResolveTranslation(key, existing);

        return new GlossaryEntry
        {
            Key = key,
            Readable = RussianText.Normalize(key),
            Suggestion = translation.Text,
            Source = translation.Source,
            Occurrences = occurrences,
            Schedules = scheduleNames.ToList()
        };
    }

    private static GlossaryTranslation ResolveTranslation(
        string key,
        IReadOnlyDictionary<string, GlossaryTranslation> existing)
    {
        if (existing.TryGetValue(key, out var carried))
        {
            return carried;
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

    private bool TryActivate(View view)
    {
        try
        {
            uiDocument.ActiveView = view;
            return true;
        }
        catch (RevitException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
