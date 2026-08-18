using System;

namespace Juru.RevitPlugins.Schedules.Models;

/// <summary>
///     Ячейка напечатанной спецификации: текст и всё, что нужно, чтобы нарисовать её заново.
///     Хранится только левая верхняя ячейка объединённой области.
/// </summary>
[Serializable]
public sealed record ScheduleTableCell
{
    public required int Row { get; init; }
    public required int Column { get; init; }
    public required int RowSpan { get; init; }
    public required int ColumnSpan { get; init; }
    public required string Text { get; init; }

    /// <summary>Высота текста в пикселях при 96 DPI: единицы стиля ячейки в API Revit.</summary>
    public required double TextSize { get; init; }

    public required bool IsBold { get; init; }
    public required bool IsItalic { get; init; }
    public required CellAlignment Alignment { get; init; }

    public required CellBorderStyles Borders { get; init; }
}
