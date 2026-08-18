using Autodesk.Revit.DB;

namespace Juru.RevitPlugins.Schedules.Models;

/// <summary>
///     Итог отрисовки одной спецификации в чертёжном виде.
/// </summary>
public sealed record DraftingTableResult
{
    public required string ViewName { get; init; }
    public required ElementId ViewId { get; init; }

    /// <summary>Габарит нарисованной таблицы в футах: ширина как в оригинале, высота с раздвинутыми строками.</summary>
    public required double WidthInFeet { get; init; }

    public required double HeightInFeet { get; init; }

    public required int LineCount { get; init; }
    public required int TextNoteCount { get; init; }

    /// <summary>Ячейки, текст которых Revit отказался разместить.</summary>
    public required int SkippedCellCount { get; init; }

    /// <summary>Первая ошибка размещения текста.</summary>
    public string? FirstSkipReason { get; init; }

    /// <summary>Что произошло с высотой текста: единицы стиля ячейки в API не документированы.</summary>
    public string? TextSizeNote { get; init; }
}
