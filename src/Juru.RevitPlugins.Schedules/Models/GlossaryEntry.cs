using System;
using System.Collections.Generic;

namespace Juru.RevitPlugins.Schedules.Models;

/// <summary>
///     Строка глоссария. Ключ — текст ячейки в точности так, как его выдаёт спецификация: по нему идёт
///     подстановка перевода, поэтому менять его нельзя даже ради читаемости.
/// </summary>
[Serializable]
public sealed record GlossaryEntry
{
    public required string Key { get; init; }

    /// <summary>Тот же текст без ручных переносов и двойных пробелов: показывается переводчику.</summary>
    public required string Readable { get; init; }

    /// <summary>
    ///     Предзаполненный перевод. Берётся из уже двуязычных строк вида «Sheet number / Номер листа»,
    ///     где английская половина есть в самом тексте.
    /// </summary>
    public required string Suggestion { get; init; }

    public required int Occurrences { get; init; }
    public required IReadOnlyList<string> Schedules { get; init; }
}
