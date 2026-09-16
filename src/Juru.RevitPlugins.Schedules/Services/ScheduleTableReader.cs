using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Schedules.Models;

using RevitException = Autodesk.Revit.Exceptions.ApplicationException;

namespace Juru.RevitPlugins.Schedules.Services;

/// <summary>
///     Снимает с спецификации напечатанную таблицу: текст, размеры и объединения ячеек.
/// </summary>
public sealed class ScheduleTableReader
{
    private static readonly SectionType[] OrderedSections = [SectionType.Header, SectionType.Body];

    /// <summary>
    ///     Тело спецификации не пересчитывается, пока вид закрыт, поэтому перед чтением вызывается
    ///     <c>RefreshData</c>. Метод обязан выполняться внутри открытой транзакции.
    /// </summary>
    /// <remarks>
    ///     Без <c>Regenerate</c> намеренно: он здесь стоял, но на текст таблицы не влияет вовсе -
    ///     проверено на модели с 71 спецификацией, 630 непустых ячеек с ним и без него. Зато на
    ///     спецификацию он стоит около 160 мс, то есть одиннадцать секунд на открытие окна, а
    ///     собранный в один вызов на всю пачку - пять. <c>RefreshData</c> пересчитывает таблицу сам,
    ///     и все 71 обходятся в 12 мс.
    /// </remarks>
    /// <param name="exportedBodyText">
    ///     Текст ячеек тела из <c>ViewSchedule.Export</c>. Таблица вида отдаёт его не полностью, поэтому
    ///     выгрузка имеет преимущество везде, где она непуста.
    /// </param>
    public ScheduleTableSnapshot Read(ViewSchedule schedule, IReadOnlyList<IReadOnlyList<string>>? exportedBodyText)
    {
        if (schedule is null)
        {
            throw new ArgumentNullException(nameof(schedule));
        }

        schedule.RefreshData();

        var sections = new List<ScheduleTableSection>();

        foreach (var sectionType in OrderedSections)
        {
            var exported = sectionType is SectionType.Body ? exportedBodyText : null;
            var section = ReadSection(schedule, sectionType, exported);

            if (section is not null)
            {
                sections.Add(section);
            }
        }

        return new ScheduleTableSnapshot
        {
            ScheduleName = schedule.Name,
            Sections = sections
        };
    }

    private static ScheduleTableSection? ReadSection(
        ViewSchedule schedule,
        SectionType sectionType,
        IReadOnlyList<IReadOnlyList<string>>? exportedText)
    {
        TableSectionData data;

        try
        {
            data = schedule.GetTableData().GetSectionData(sectionType);
        }
        catch (RevitException)
        {
            return null;
        }

        var rowCount = data.NumberOfRows;
        var columnCount = data.NumberOfColumns;

        if (rowCount is 0 || columnCount is 0)
        {
            return null;
        }

        var mergeAnchors = ReadMergeAnchors(data, rowCount, columnCount);
        var table = ReadTextGrid(data, rowCount, columnCount);

        return new ScheduleTableSection
        {
            SectionName = sectionType.ToString(),
            ColumnWidths = ReadColumnWidths(data, columnCount),
            RowHeights = ReadRowHeights(data, rowCount),
            MergeAnchors = mergeAnchors,
            Cells = ReadCells(data, rowCount, columnCount, mergeAnchors, table, Align(exportedText, table, columnCount))
        };
    }

    private static IReadOnlyList<double> ReadColumnWidths(TableSectionData data, int columnCount)
    {
        var widths = new List<double>(columnCount);

        for (var column = 0; column < columnCount; column++)
        {
            widths.Add(data.GetColumnWidth(column));
        }

        return widths;
    }

    private static IReadOnlyList<double> ReadRowHeights(TableSectionData data, int rowCount)
    {
        var heights = new List<double>(rowCount);

        for (var row = 0; row < rowCount; row++)
        {
            heights.Add(data.GetRowHeight(row));
        }

        return heights;
    }

    private static IReadOnlyList<IReadOnlyList<int>> ReadMergeAnchors(
        TableSectionData data,
        int rowCount,
        int columnCount)
    {
        var anchors = new List<IReadOnlyList<int>>(rowCount);

        for (var row = 0; row < rowCount; row++)
        {
            var rowAnchors = new List<int>(columnCount);

            for (var column = 0; column < columnCount; column++)
            {
                var merged = ReadMergedCell(data, row, column);
                var anchorRow = merged?.Top ?? row;
                var anchorColumn = merged?.Left ?? column;

                rowAnchors.Add(anchorRow * columnCount + anchorColumn);
            }

            anchors.Add(rowAnchors);
        }

        return anchors;
    }

    private static TableMergedCell? ReadMergedCell(TableSectionData data, int row, int column)
    {
        try
        {
            return data.GetMergedCell(row, column);
        }
        catch (RevitException)
        {
            return null;
        }
    }

    /// <summary>
    ///     Прикладывает выгрузку к таблице и раскладывает её строки по строкам таблицы; <c>null</c> там,
    ///     где выгрузка строку не покрывает, и целиком, если приложить её не удалось.
    /// </summary>
    /// <remarks>
    ///     Число строк совпадает не всегда: некоторым спецификациям Revit не выгружает строку заголовков
    ///     колонок, и выгрузка оказывается ровно на строку короче. Требовать точного совпадения нельзя -
    ///     тогда такая выгрузка отбрасывается целиком, а таблица вида отдаёт текст не всех ячеек, и в
    ///     копии остаются только заголовки да первая колонка. Но и брать сдвиг на веру нельзя: угаданный
    ///     неверно, он развёл бы текст по чужим ячейкам молча. Поэтому сдвиг подбирается по совпадению с
    ///     тем текстом, который таблица всё-таки отдаёт, и неподтверждённый не применяется.
    /// </remarks>
    private static IReadOnlyList<string>?[]? Align(
        IReadOnlyList<IReadOnlyList<string>>? exportedText,
        string[][] table,
        int columnCount)
    {
        if (exportedText is null || exportedText.Count is 0)
        {
            return null;
        }

        // Разъехавшиеся колонки сдвигом строк не выправить, и сопоставить их нечем.
        if (!exportedText.All(row => row.Count == columnCount))
        {
            return null;
        }

        if (exportedText.Count == table.Length)
        {
            return Place(exportedText, table.Length, 0);
        }

        var best = 0;
        var bestOffset = 0;

        foreach (var offset in Offsets(table.Length - exportedText.Count))
        {
            var score = Score(table, exportedText, offset);

            if (score > best)
            {
                best = score;
                bestOffset = offset;
            }
        }

        return best > 0 ? Place(exportedText, table.Length, bestOffset) : null;
    }

    /// <summary>Сдвиги от нуля к краю: при равном совпадении меньший сдвиг вернее.</summary>
    private static IEnumerable<int> Offsets(int difference)
    {
        var step = difference >= 0 ? 1 : -1;

        for (var offset = 0; offset != difference + step; offset += step)
        {
            yield return offset;
        }
    }

    /// <summary>Сколько непустых ячеек таблицы совпало с выгрузкой при этом сдвиге.</summary>
    private static int Score(string[][] table, IReadOnlyList<IReadOnlyList<string>> exportedText, int offset)
    {
        var matched = 0;

        for (var row = 0; row < exportedText.Count; row++)
        {
            var target = row + offset;

            if (target < 0 || target >= table.Length)
            {
                continue;
            }

            for (var column = 0; column < table[target].Length; column++)
            {
                var known = table[target][column].Trim();

                if (known.Length > 0
                    && string.Equals(known, exportedText[row][column].Trim(), StringComparison.CurrentCulture))
                {
                    matched++;
                }
            }
        }

        return matched;
    }

    private static IReadOnlyList<string>?[] Place(
        IReadOnlyList<IReadOnlyList<string>> exportedText,
        int rowCount,
        int offset)
    {
        var placed = new IReadOnlyList<string>?[rowCount];

        for (var row = 0; row < exportedText.Count; row++)
        {
            var target = row + offset;

            if (target >= 0 && target < rowCount)
            {
                placed[target] = exportedText[row];
            }
        }

        return placed;
    }

    private static string[][] ReadTextGrid(TableSectionData data, int rowCount, int columnCount)
    {
        var grid = new string[rowCount][];

        for (var row = 0; row < rowCount; row++)
        {
            grid[row] = new string[columnCount];

            for (var column = 0; column < columnCount; column++)
            {
                grid[row][column] = ReadCellText(data, row, column);
            }
        }

        return grid;
    }

    private static IReadOnlyList<ScheduleTableCell> ReadCells(
        TableSectionData data,
        int rowCount,
        int columnCount,
        IReadOnlyList<IReadOnlyList<int>> mergeAnchors,
        string[][] table,
        IReadOnlyList<string>?[]? exportedText)
    {
        var cells = new List<ScheduleTableCell>();

        for (var row = 0; row < rowCount; row++)
        {
            for (var column = 0; column < columnCount; column++)
            {
                // Объединённая область даёт один текст, привязанный к левой верхней ячейке.
                if (mergeAnchors[row][column] != row * columnCount + column)
                {
                    continue;
                }

                var merged = ReadMergedCell(data, row, column);
                var style = ReadCellStyle(data, row, column);

                cells.Add(new ScheduleTableCell
                {
                    Row = row,
                    Column = column,
                    RowSpan = merged is null ? 1 : merged.Bottom - merged.Top + 1,
                    ColumnSpan = merged is null ? 1 : merged.Right - merged.Left + 1,
                    Text = ResolveText(table, exportedText, row, column),
                    TextSize = style?.TextSize ?? 0,
                    IsBold = style?.IsFontBold ?? false,
                    IsItalic = style?.IsFontItalic ?? false,
                    Alignment = MapAlignment(style?.FontHorizontalAlignment),
                    Borders = ReadBorders(style)
                });
            }
        }

        return cells;
    }

    /// <summary>
    ///     Текст ячейки: выгрузка, а где она молчит - таблица вида. Выгрузка вернее, потому что таблица
    ///     отдаёт текст не всех ячеек, но и в выгрузке бывает пусто там, где таблица текст знает -
    ///     например в строке заголовков, которой в выгрузке может не оказаться вовсе.
    /// </summary>
    private static string ResolveText(
        string[][] table,
        IReadOnlyList<string>?[]? exportedText,
        int row,
        int column)
    {
        var exported = exportedText?[row]?[column] ?? string.Empty;

        return exported.Length > 0 ? exported : table[row][column];
    }

    private static CellBorderStyles ReadBorders(TableCellStyle? style)
    {
        return new CellBorderStyles
        {
            Left = GetIdValue(style?.BorderLeftLineStyle),
            Right = GetIdValue(style?.BorderRightLineStyle),
            Top = GetIdValue(style?.BorderTopLineStyle),
            Bottom = GetIdValue(style?.BorderBottomLineStyle)
        };
    }

    private static long GetIdValue(ElementId? elementId)
    {
        if (elementId is null)
        {
            return -1;
        }

#if REVIT2024_OR_GREATER
        return elementId.Value;
#else
        return elementId.IntegerValue;
#endif
    }

    private static TableCellStyle? ReadCellStyle(TableSectionData data, int row, int column)
    {
        try
        {
            return data.GetTableCellStyle(row, column);
        }
        catch (RevitException)
        {
            return null;
        }
    }

    private static string ReadCellText(TableSectionData data, int row, int column)
    {
        try
        {
            return data.GetCellText(row, column) ?? string.Empty;
        }
        catch (RevitException)
        {
            return string.Empty;
        }
    }

    private static CellAlignment MapAlignment(HorizontalAlignmentStyle? alignment)
    {
        return alignment switch
        {
            HorizontalAlignmentStyle.Center => CellAlignment.Center,
            HorizontalAlignmentStyle.Right => CellAlignment.Right,
            _ => CellAlignment.Left
        };
    }
}
