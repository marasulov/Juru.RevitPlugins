using System;
using System.Collections.Generic;
using System.Linq;

using CommunityToolkit.Mvvm.ComponentModel;

using Juru.RevitPlugins.Schedules.Models;
using Juru.RevitPlugins.Schedules.Services;

namespace Juru.RevitPlugins.Schedules.ViewModels;

/// <summary>
///     Строка таблицы переводов: один текст спецификации. Одинаковый текст в разных спецификациях —
///     одна строка, потому что подстановка идёт по тексту, а не по месту.
/// </summary>
public sealed partial class TranslationRowViewModel : ObservableObject
{
    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private string translation;

    private TranslationRowViewModel(string sourceText, string kind, string translation, IEnumerable<string> schedules)
    {
        SourceText = sourceText;
        Readable = RussianText.Normalize(sourceText);
        Kind = kind;
        this.translation = translation;
        Schedules = string.Join(", ", schedules);
        isSelected = translation.Length is 0;
    }

    public string SourceText { get; }

    /// <summary>Текст без ручных переносов: заголовок «Кат. поме-  ще-  ния» читается как «Кат. помещения».</summary>
    public string Readable { get; }

    /// <summary>Чем текст является в таблице: названием, заголовком колонки или значением ячейки.</summary>
    public string Kind { get; }

    public string Schedules { get; }

    public bool HasTranslation
    {
        get { return Translation.Length > 0; }
    }

    /// <summary>
    ///     Собирает строки по снимкам: название спецификации, заголовки колонок и значения ячеек.
    ///     Одинаковые тексты сливаются, места их появления перечисляются.
    /// </summary>
    public static IReadOnlyList<TranslationRowViewModel> Build(
        IReadOnlyList<ReadSchedule> schedules,
        IReadOnlyDictionary<string, string> glossary)
    {
        var kinds = new Dictionary<string, string>(StringComparer.CurrentCulture);
        var places = new Dictionary<string, SortedSet<string>>(StringComparer.CurrentCulture);

        foreach (var read in schedules)
        {
            if (read.Snapshot is null)
            {
                continue;
            }

            foreach (var section in read.Snapshot.Sections)
            {
                foreach (var cell in section.Cells)
                {
                    Add(kinds, places, cell, section, read.Source.Schedule.Name);
                }
            }
        }

        return kinds
            .Select(pair => new TranslationRowViewModel(
                pair.Key,
                pair.Value,
                glossary.TryGetValue(pair.Key, out var translation) ? translation : string.Empty,
                places[pair.Key]))
            .OrderBy(row => row.Kind, StringComparer.CurrentCulture)
            .ThenBy(row => row.Readable, StringComparer.CurrentCulture)
            .ToList();
    }

    private static void Add(
        Dictionary<string, string> kinds,
        Dictionary<string, SortedSet<string>> places,
        ScheduleTableCell cell,
        ScheduleTableSection section,
        string scheduleName)
    {
        var text = cell.Text.Trim();

        if (!RussianText.ContainsCyrillic(text) || RussianText.IsSpacerLabel(text))
        {
            return;
        }

        kinds[text] = DescribeKind(cell, section);

        if (!places.TryGetValue(text, out var names))
        {
            names = new SortedSet<string>(StringComparer.CurrentCulture);
            places[text] = names;
        }

        names.Add(scheduleName);
    }

    /// <summary>
    ///     Нулевая строка тела несёт заголовки колонок, секция заголовка — название спецификации.
    /// </summary>
    private static string DescribeKind(ScheduleTableCell cell, ScheduleTableSection section)
    {
        if (!string.Equals(section.SectionName, "Body", StringComparison.Ordinal))
        {
            return "название";
        }

        return cell.Row is 0 ? "заголовок колонки" : "значение";
    }
}
