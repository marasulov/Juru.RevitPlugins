using System.Collections.Generic;
using System.Linq;

using CommunityToolkit.Mvvm.ComponentModel;

using Juru.RevitPlugins.Schedules.Services;

using Juru.RevitPlugins.Schedules.Localization;

namespace Juru.RevitPlugins.Schedules.ViewModels;

/// <summary>
///     Строка таблицы выбора: одна спецификация с листов. Снимок держится при себе — по нему потом рисуется
///     копия, и читать спецификацию второй раз не нужно.
/// </summary>
public sealed partial class ScheduleRowViewModel : ObservableObject, ISelectableRow
{
    [ObservableProperty]
    private bool isSelected;

    /// <summary>Строка состояния считается по этому счёту, поэтому меняются они вместе.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    private int untranslatedCount;

    /// <summary>Различные строки с русским текстом: по ним считается «Без перевода».</summary>
    private readonly HashSet<string> translatable;

    public ScheduleRowViewModel(ReadSchedule read, IReadOnlyDictionary<string, string> glossary)
    {
        Read = read;

        var described = read.Described;

        ScheduleName = described.ScheduleName;
        // Пусто в этой колонке читалось бы как «не прочитали», поэтому отсутствие листа названо
        // словами: спецификация в списке есть, а на листах её нет.
        Sheets = described.SheetNumbers.Count is 0
            ? Text.NotOnSheet
            : string.Join(", ", described.SheetNumbers);
        ColumnCount = described.VisibleColumnHeadings.Count;
        RowCount = described.RowCount;
        Error = described.Error;

        translatable = CollectTranslatable(read);

        TranslatableCount = translatable.Count;
        untranslatedCount = translatable.Count(text => !glossary.ContainsKey(text));
    }

    /// <summary>
    ///     Пересчитывает «Без перевода» по изменившемуся словарю. Строки таблицы при этом не
    ///     пересоздаются: галочки и выделение остались бы позади, а перечитывать модель незачем -
    ///     поменялся словарь, а не модель.
    /// </summary>
    public void RefreshAgainst(IReadOnlyDictionary<string, string> glossary)
    {
        UntranslatedCount = translatable.Count(text => !glossary.ContainsKey(text));
    }

    public ReadSchedule Read { get; }

    public string ScheduleName { get; }
    public string Sheets { get; }
    public int ColumnCount { get; }
    public int RowCount { get; }

    /// <summary>Различных строк с русским текстом в таблице.</summary>
    public int TranslatableCount { get; }

    public string? Error { get; }

    public string Status
    {
        get
        {
            if (Error is not null)
            {
                return Text.StatusReadError;
            }

            if (TranslatableCount is 0)
            {
                return Text.StatusNothingToTranslate;
            }

            return UntranslatedCount is 0
                ? Text.StatusTranslated
                : $"{Text.StatusNeedsTranslation}: {UntranslatedCount}";
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
