using System;
using System.Collections.Generic;

namespace Juru.RevitPlugins.Schedules.Models;

/// <summary>
///     Разбор одной спецификации модели.
/// </summary>
[Serializable]
public sealed record ScheduleAudit
{
    public required string ScheduleName { get; init; }
    public required string CategoryName { get; init; }
    public required bool IsKeySchedule { get; init; }

    /// <summary>Сколько раз спецификация размещена на листах: приоритет перевода.</summary>
    public required int SheetPlacementCount { get; init; }

    /// <summary>Сколько элементов попало в спецификацию и дало значения колонок.</summary>
    public required int ElementCount { get; init; }

    public required IReadOnlyList<ScheduleFieldAudit> Fields { get; init; }

    /// <summary>Поля сортировки и группировки: в английской версии должны ссылаться на En-поля.</summary>
    public required IReadOnlyList<string> SortGroupFieldNames { get; init; }

    public required IReadOnlyList<ScheduleFilterAudit> Filters { get; init; }

    /// <summary>Причина, по которой значения ячеек не собраны.</summary>
    public string? ValueHarvestWarning { get; init; }
}
