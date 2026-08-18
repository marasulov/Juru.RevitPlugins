using System;
using System.Collections.Generic;

namespace Juru.RevitPlugins.Schedules.Models;

/// <summary>
///     Секция таблицы спецификации. Секции идут сверху вниз и имеют собственную разбивку на колонки.
/// </summary>
[Serializable]
public sealed record ScheduleTableSection
{
    public required string SectionName { get; init; }

    /// <summary>Ширины колонок в футах, слева направо.</summary>
    public required IReadOnlyList<double> ColumnWidths { get; init; }

    /// <summary>Высоты строк в футах, сверху вниз.</summary>
    public required IReadOnlyList<double> RowHeights { get; init; }

    public required IReadOnlyList<ScheduleTableCell> Cells { get; init; }

    /// <summary>
    ///     Для каждой ячейки <c>[строка][колонка]</c> — номер левой верхней ячейки объединённой области,
    ///     к которой она принадлежит, закодированный как <c>строка * количество колонок + колонка</c>.
    ///     Совпадение номеров у соседей означает, что границы между ними нет.
    /// </summary>
    public required IReadOnlyList<IReadOnlyList<int>> MergeAnchors { get; init; }
}
