using System.Collections.Generic;
using System.Linq;

namespace Juru.RevitPlugins.Openings.Models.Configs;

/// <summary>
/// Настройки выдачи заданий на отверстия для одного вида инженерных элементов.
/// </summary>
public class MepCategorySettings {
    /// <summary>
    /// Вид инженерных элементов, к которому относятся настройки.
    /// </summary>
    public MepKind Kind { get; set; }

    /// <summary>
    /// Участвует ли вид в поиске пересечений.
    /// </summary>
    public bool IsSelected { get; set; } = true;

    /// <summary>
    /// Наименьший габарит элемента в мм, для которого ещё нужно отверстие.
    /// Для круглых элементов это диаметр, для прямоугольных - большая сторона сечения.
    /// </summary>
    public double MinSizeMm { get; set; } = 50;

    /// <summary>
    /// Наибольший габарит элемента в мм. Всё крупнее считается ошибкой черчения и пропускается.
    /// </summary>
    public double MaxSizeMm { get; set; } = 10000;

    /// <summary>
    /// Конструкции, которые проверяются на пересечение с элементами этого вида.
    /// </summary>
    public List<StructureKind> Structures { get; set; } = [StructureKind.Wall, StructureKind.Floor];

    /// <summary>
    /// Зазоры и формы отверстий по диапазонам габаритов элемента.
    /// </summary>
    public List<OffsetRange> Offsets { get; set; } = [];

    /// <summary>
    /// Округление габаритов задания в мм.
    /// </summary>
    public int SizeRoundingMm { get; set; } = 50;

    /// <summary>
    /// Округление отметки низа задания в мм.
    /// </summary>
    public int ElevationRoundingMm { get; set; } = 10;

    /// <summary>
    /// Возвращает правило зазора для габарита элемента, либо <c>null</c>,
    /// если габарит не попал ни в один диапазон.
    /// </summary>
    /// <param name="sizeMm">Габарит инженерного элемента в мм.</param>
    public OffsetRange GetOffsetRange(double sizeMm) {
        return Offsets.FirstOrDefault(offset => offset.Contains(sizeMm));
    }

    /// <summary>
    /// Проверяет, нужно ли выдавать задание для элемента такого габарита.
    /// </summary>
    /// <param name="sizeMm">Габарит инженерного элемента в мм.</param>
    public bool SizeInRange(double sizeMm) {
        return sizeMm >= MinSizeMm && sizeMm <= MaxSizeMm;
    }
}
