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
public sealed class ScheduleTableReader(Document document)
{
    private static readonly SectionType[] OrderedSections = [SectionType.Header, SectionType.Body];

    /// <summary>
    ///     Тело спецификации не пересчитывается, пока вид закрыт, поэтому перед чтением вызывается
    ///     <c>RefreshData</c>. Метод обязан выполняться внутри открытой транзакции.
    /// </summary>
    /// <param name="exportedBodyText">
    ///     Текст ячеек тела из <c>ViewSchedule.Export</c>. Таблица вида отдаёт его не полностью, поэтому при
    ///     совпадении размеров он замещает прочитанный из таблицы.
    /// </param>
    public ScheduleTableSnapshot Read(ViewSchedule schedule, IReadOnlyList<IReadOnlyList<string>>? exportedBodyText)
    {
        if (schedule is null)
        {
            throw new ArgumentNullException(nameof(schedule));
        }

        schedule.RefreshData();
        document.Regenerate();

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

        return new ScheduleTableSection
        {
            SectionName = sectionType.ToString(),
            ColumnWidths = ReadColumnWidths(data, columnCount),
            RowHeights = ReadRowHeights(data, rowCount),
            MergeAnchors = mergeAnchors,
            Cells = ReadCells(data, rowCount, columnCount, mergeAnchors, MatchExport(exportedText, rowCount, columnCount))
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
    ///     Выгрузка используется только когда её размеры совпадают с таблицей: иначе строки разъедутся и текст
    ///     попадёт в чужие ячейки.
    /// </summary>
    private static IReadOnlyList<IReadOnlyList<string>>? MatchExport(
        IReadOnlyList<IReadOnlyList<string>>? exportedText,
        int rowCount,
        int columnCount)
    {
        if (exportedText is null || exportedText.Count != rowCount)
        {
            return null;
        }

        return exportedText.All(row => row.Count == columnCount) ? exportedText : null;
    }

    private static IReadOnlyList<ScheduleTableCell> ReadCells(
        TableSectionData data,
        int rowCount,
        int columnCount,
        IReadOnlyList<IReadOnlyList<int>> mergeAnchors,
        IReadOnlyList<IReadOnlyList<string>>? exportedText)
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
                    Text = exportedText is null
                        ? ReadCellText(data, row, column)
                        : exportedText[row][column],
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
