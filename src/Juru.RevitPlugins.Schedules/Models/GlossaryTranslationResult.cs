using System.Collections.Generic;

namespace Juru.RevitPlugins.Schedules.Models;

/// <summary>
///     Итог машинного перевода глоссария.
/// </summary>
public sealed record GlossaryTranslationResult
{
    public required IReadOnlyList<GlossaryEntry> Entries { get; init; }
    public required int TranslatedCount { get; init; }

    /// <summary>
    ///     Переводы, отклонённые из-за расхождения чисел с оригиналом: строки остались без перевода.
    /// </summary>
    public required IReadOnlyList<string> RejectedTexts { get; init; }

    /// <summary>Запросы, которые не удались: соответствующие строки остались без перевода.</summary>
    public required IReadOnlyList<string> Failures { get; init; }
}
