using System;
using System.Collections.Generic;

namespace Juru.RevitPlugins.Schedules.Models;

/// <summary>
///     Снимок напечатанной спецификации, из которого таблица рисуется заново в чертёжном виде.
/// </summary>
[Serializable]
public sealed record ScheduleTableSnapshot
{
    public required string ScheduleName { get; init; }
    public required IReadOnlyList<ScheduleTableSection> Sections { get; init; }
}
