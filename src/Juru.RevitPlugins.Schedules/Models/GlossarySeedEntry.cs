using System;
using System.Collections.Generic;

namespace Juru.RevitPlugins.Schedules.Models;

/// <summary>
///     Строка исходного текста, ожидающая перевода, и места, где она встречается.
/// </summary>
[Serializable]
public sealed record GlossarySeedEntry
{
    public required string SourceText { get; init; }

    /// <summary>Что это за текст: заголовок колонки, значение ячейки, разделитель или значение фильтра.</summary>
    public required GlossarySeedOrigin Origin { get; init; }

    public required IReadOnlyList<string> Usages { get; init; }
}
