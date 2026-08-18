using System;

namespace Juru.RevitPlugins.Schedules.Models;

/// <summary>
///     Идентификаторы стилей линий границ ячейки. Спецификация скрывает часть границ, поэтому по ним
///     определяется, какие линии вообще нужно рисовать.
/// </summary>
[Serializable]
public sealed record CellBorderStyles
{
    public required long Left { get; init; }
    public required long Right { get; init; }
    public required long Top { get; init; }
    public required long Bottom { get; init; }
}
