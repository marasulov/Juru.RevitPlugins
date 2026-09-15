using System;
using System.Collections.Generic;

namespace Juru.RevitPlugins.Schedules.Models;

/// <summary>
///     Итог записи переводов в модель по одной категории.
/// </summary>
[Serializable]
public sealed record ElementTextApplyResult
{
    public required string Category { get; init; }

    /// <summary>Строк, в которые перевод записан.</summary>
    public int WrittenCount { get; init; }

    /// <summary>Строк, возвращённых к русскому исходнику.</summary>
    public int RevertedCount { get; init; }

    /// <summary>Пропущено: перевод совпал с тем, что уже стоит в параметре.</summary>
    public int UnchangedCount { get; init; }

    /// <summary>Элемент не изменить: занят другим пользователем или удалён после чтения.</summary>
    public IReadOnlyList<string> Failures { get; init; } = [];
}
