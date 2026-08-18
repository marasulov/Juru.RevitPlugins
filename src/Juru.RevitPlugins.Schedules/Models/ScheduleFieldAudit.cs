using System;
using System.Collections.Generic;

namespace Juru.RevitPlugins.Schedules.Models;

/// <summary>
///     Разбор одной колонки спецификации.
/// </summary>
[Serializable]
public sealed record ScheduleFieldAudit
{
    public required int Position { get; init; }
    public required string FieldName { get; init; }
    public required string ColumnHeading { get; init; }
    public required string FieldType { get; init; }
    public required bool IsHidden { get; init; }
    public required ScheduleMirroringKind MirroringKind { get; init; }
    public required bool RequiresTranslation { get; init; }

    public string? ParameterName { get; init; }
    public bool IsBuiltInParameter { get; init; }

    /// <summary>Параметры, из которых собрана комбинированная колонка, в порядке следования.</summary>
    public IReadOnlyList<string> CombinedParameterNames { get; init; } = [];

    /// <summary>Префиксы, разделители и суффиксы комбинированной колонки: переводятся отдельно от параметров.</summary>
    public IReadOnlyList<string> CombinedParameterLiterals { get; init; } = [];

    public int DistinctValueCount { get; init; }
    public IReadOnlyList<string> DistinctValues { get; init; } = [];
    public bool DistinctValuesTruncated { get; init; }
}
