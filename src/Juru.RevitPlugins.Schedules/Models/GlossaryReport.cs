using System.Collections.Generic;

namespace Juru.RevitPlugins.Schedules.Models;

/// <summary>
///     Что случилось с глоссарием за прогон: сколько строк откуда взялось и что осталось без перевода.
/// </summary>
public sealed record GlossaryReport
{
    public required int TotalCount { get; init; }
    public required int BaseTermCount { get; init; }
    public required int FromBaseCount { get; init; }
    public required int MachineCount { get; init; }
    public required int TranslatedNowCount { get; init; }
    public required int UntranslatedCount { get; init; }

    /// <summary>Переводы, отклонённые из-за расхождения чисел с оригиналом.</summary>
    public required int RejectedCount { get; init; }

    public required string TranslatorNote { get; init; }
    public required IReadOnlyList<string> Failures { get; init; }
}
