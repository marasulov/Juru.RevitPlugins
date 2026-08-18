namespace Juru.RevitPlugins.Openings.Models.Configs;

/// <summary>
/// Правило для диапазона габаритов инженерного элемента: какой зазор оставить вокруг него
/// и какой формой резать конструкцию.
/// </summary>
public class OffsetRange {
    /// <summary>
    /// Нижняя граница габарита элемента в мм, включительно.
    /// </summary>
    public double FromMm { get; set; }

    /// <summary>
    /// Верхняя граница габарита элемента в мм, включительно.
    /// </summary>
    public double ToMm { get; set; }

    /// <summary>
    /// Зазор в мм с одной стороны элемента. В габарит отверстия входит дважды.
    /// </summary>
    public double OffsetMm { get; set; }

    /// <summary>
    /// Форма отверстия для этого диапазона.
    /// </summary>
    public OpeningShape Shape { get; set; } = OpeningShape.Rectangle;

    /// <summary>
    /// Проверяет, попадает ли габарит элемента в этот диапазон.
    /// </summary>
    /// <param name="sizeMm">Габарит инженерного элемента в мм.</param>
    public bool Contains(double sizeMm) {
        return sizeMm >= FromMm && sizeMm <= ToMm;
    }
}
