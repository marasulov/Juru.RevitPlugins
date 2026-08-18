using System.Collections.Generic;

namespace Juru.RevitPlugins.Schedules.Models;

/// <summary>
///     Итог сбора глоссария по листовым спецификациям.
/// </summary>
public sealed record GlossaryCollectionResult
{
    public required int ScheduleCount { get; init; }
    public required IReadOnlyList<GlossaryEntry> Entries { get; init; }

    /// <summary>Спецификации, которые не удалось прочитать.</summary>
    public required IReadOnlyList<string> Failures { get; init; }
}
