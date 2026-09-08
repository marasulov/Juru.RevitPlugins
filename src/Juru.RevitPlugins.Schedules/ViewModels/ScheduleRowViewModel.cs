using System.Collections.Generic;
using System.Linq;

using CommunityToolkit.Mvvm.ComponentModel;

using Juru.RevitPlugins.Schedules.Services;

namespace Juru.RevitPlugins.Schedules.ViewModels;

/// <summary>
///     Строка таблицы выбора: одна спецификация с листов. Снимок держится при себе — по нему потом рисуется
///     копия, и читать спецификацию второй раз не нужно.
/// </summary>
public sealed partial class ScheduleRowViewModel : ObservableObject
{
    [ObservableProperty]
    private bool isSelected;

    public ScheduleRowViewModel(ReadSchedule read, IReadOnlyDictionary<string, string> glossary)
    {
        Read = read;

        var described = read.Described;

        ScheduleName = described.ScheduleName;
        Sheets = string.Join(", ", described.SheetNumbers);
        ColumnCount = described.VisibleColumnHeadings.Count;
        RowCount = described.RowCount;
        Error = described.Error;

        var translatable = CollectTranslatable(read);

        TranslatableCount = translatable.Count;
        UntranslatedCount = translatable.Count(text => !glossary.ContainsKey(text));
    }

    public ReadSchedule Read { get; }

    public string ScheduleName { get; }
    public string Sheets { get; }
    public int ColumnCount { get; }
    public int RowCount { get; }

    /// <summary>Различных строк с русским текстом в таблице.</summary>
    public int TranslatableCount { get; }

    /// <summary>Из них тех, которых ещё нет в глоссарии.</summary>
    public int UntranslatedCount { get; }

    public string? Error { get; }

    public string Status
    {
        get
        {
            if (Error is not null)
            {
                return "ошибка чтения";
            }

            if (TranslatableCount is 0)
            {
                return "переводить нечего";
            }

            return UntranslatedCount is 0 ? "переведена" : $"нужен перевод: {UntranslatedCount}";
        }
    }

    /// <summary>
    ///     Отбирает строки по тому же правилу, что и сбор глоссария: только русский текст, без подписей
    ///     колонок-распорок.
    /// </summary>
    private static HashSet<string> CollectTranslatable(ReadSchedule read)
    {
        var texts = new HashSet<string>(System.StringComparer.CurrentCulture);

        if (read.Snapshot is null)
        {
            return texts;
        }

        foreach (var cell in read.Snapshot.Sections.SelectMany(section => section.Cells))
        {
            var text = cell.Text.Trim();

            if (RussianText.ContainsCyrillic(text) && !RussianText.IsSpacerLabel(text))
            {
                texts.Add(text);
            }
        }

        return texts;
    }
}
