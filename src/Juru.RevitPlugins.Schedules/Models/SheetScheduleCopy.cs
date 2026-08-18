using System;
using System.Collections.Generic;

namespace Juru.RevitPlugins.Schedules.Models;

/// <summary>
///     Результат копирования одной листовой спецификации в чертёжный вид.
/// </summary>
[Serializable]
public sealed record SheetScheduleCopy
{
    public required string ScheduleName { get; init; }

    /// <summary>Листы, на которых размещена спецификация.</summary>
    public required IReadOnlyList<string> SheetNumbers { get; init; }

    /// <summary>Заголовки колонок, видимых на листе, слева направо.</summary>
    public required IReadOnlyList<string> VisibleColumnHeadings { get; init; }

    /// <summary>Скрытые поля: в таблицу на листе они не попадают.</summary>
    public required IReadOnlyList<string> HiddenFieldNames { get; init; }

    /// <summary>Спецификация разбита на сегменты, и на листе может быть виден только один из них.</summary>
    public required bool IsSplit { get; init; }

    public string? ViewName { get; init; }

    /// <summary>Строк в теле до активации вида: расхождение с итоговым числом означает устаревшие данные.</summary>
    public int RowCountBeforeActivation { get; init; }

    /// <summary>Удалось ли открыть вид перед чтением.</summary>
    public bool WasActivated { get; init; }

    public int RowCount { get; init; }
    public double TableWidthMillimeters { get; init; }
    public int LineCount { get; init; }
    public int TextNoteCount { get; init; }
    public int SkippedCellCount { get; init; }

    /// <summary>Сколько копий от прошлых прогонов удалено перед созданием новой.</summary>
    public int ReplacedCopyCount { get; init; }

    /// <summary>Лист, на который вставлена копия рядом с оригиналом.</summary>
    public string? PlacedOnSheet { get; init; }

    /// <summary>
    ///     Листы, где копия не вставлена: чертёжный вид размещается только на одном листе, а спецификация
    ///     может стоять на нескольких.
    /// </summary>
    public IReadOnlyList<string> NotPlacedOnSheets { get; init; } = [];

    /// <summary>Строк, полученных экспортом спецификации: 0 — экспорт не дал данных.</summary>
    public int ExportedRowCount { get; init; }

    public string? ExportWarning { get; init; }

    public string? PlacementError { get; init; }
    public string? Error { get; init; }
}
