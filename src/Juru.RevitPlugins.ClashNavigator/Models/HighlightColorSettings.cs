using System;

using Autodesk.Revit.DB;

using Newtonsoft.Json;

namespace Juru.RevitPlugins.ClashNavigator.Models;

/// <summary>
/// Цвет и прозрачность, которыми подсвечивается один из элементов коллизии.
/// </summary>
/// <remarks>
/// Цвет хранится отдельными каналами, а не цветом WPF или Ревита: оба сериализуются в json
/// с лишними служебными полями, а настройки пользователь иногда правит руками.
/// </remarks>
internal class HighlightColorSettings {
    public byte Red { get; set; }

    public byte Green { get; set; }

    public byte Blue { get; set; }

    /// <summary>
    /// Прозрачность граней подсветки в процентах: 0 - непрозрачные, 100 - невидимые.
    /// Рёбра всегда рисуются непрозрачными, чтобы элемент оставался различим.
    /// </summary>
    public int Transparency { get; set; } = DefaultTransparency;

    /// <summary>
    /// Прозрачность граней по умолчанию, %.
    /// </summary>
    public const int DefaultTransparency = 35;

    /// <summary>
    /// Цвет первого элемента по умолчанию - красный.
    /// </summary>
    public static HighlightColorSettings DefaultFirst => new() { Red = 229, Green = 57, Blue = 53 };

    /// <summary>
    /// Цвет второго элемента по умолчанию - янтарный.
    /// </summary>
    /// <remarks>
    /// Не синий: элементы коллизии ещё и выделены, а выделение Ревит рисует синим поверх, -
    /// синий элемент на нём не отличить от выделенного.
    /// </remarks>
    public static HighlightColorSettings DefaultSecond => new() { Red = 255, Green = 179, Blue = 0 };

    public Color ToRevitColor() {
        return new Color(Red, Green, Blue);
    }

    /// <summary>
    /// Прозрачность граней для эффекта отрисовки: доля от 0 до 1.
    /// </summary>
    [JsonIgnore]
    public double TransparencyFraction => Math.Max(0, Math.Min(100, Transparency)) / 100d;

    public HighlightColorSettings Clone() {
        return new HighlightColorSettings {
            Red = Red,
            Green = Green,
            Blue = Blue,
            Transparency = Transparency
        };
    }
}
