using System;

namespace Juru.RevitPlugins.Schedules.Models;

/// <summary>
///     Фильтр спецификации. Строковое значение фильтра переводится вместе с полем, иначе английская
///     спецификация окажется пустой.
/// </summary>
[Serializable]
public sealed record ScheduleFilterAudit
{
    public required string FieldName { get; init; }
    public required string FilterType { get; init; }
    public string? StringValue { get; init; }
}
