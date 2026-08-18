using System;
using System.Collections.Generic;

namespace Juru.RevitPlugins.Schedules.Models;

/// <summary>
///     Полный разбор спецификаций модели: основа для оценки объёма перевода.
/// </summary>
[Serializable]
public sealed record ScheduleAuditReport
{
    public required string ModelName { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required IReadOnlyList<ScheduleAudit> Schedules { get; init; }
}
