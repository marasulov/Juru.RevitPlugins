using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Openings.Models.Intersections;

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
    /// Наименьший диаметр в мм, для которого ещё нужно отверстие. Ноль - проверять любые.
    /// </summary>
    public double MinDiameterMm { get; set; }

    /// <summary>
    /// Наименьшая ширина сечения в мм для прямоугольных элементов. Ноль - проверять любые.
    /// </summary>
    public double MinWidthMm { get; set; }

    /// <summary>
    /// Наименьшая высота сечения в мм для прямоугольных элементов. Ноль - проверять любые.
    /// </summary>
    public double MinHeightMm { get; set; }

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
    /// Проверяет, нужно ли выдавать задание для элемента с таким сечением.
    /// </summary>
    /// <remarks>
    /// Пороги задаются по каждому габариту отдельно: у круглых элементов проверяется диаметр,
    /// у прямоугольных - и ширина, и высота. Прямоугольный воздуховод 500x50 не должен проходить
    /// порог только за счёт ширины: по высоте он ниже допустимого, и отверстие для него не нужно.
    /// </remarks>
    /// <param name="size">Габариты сечения элемента.</param>
    public bool SizeInRange(MepElementSize size) {
        if(size is null) {
            return false;
        }

        double governingMm = ToMillimeters(size.GoverningSize);
        if(governingMm > MaxSizeMm) {
            return false;
        }

        return size.IsRound
            ? ToMillimeters(size.Diameter) >= MinDiameterMm
            : ToMillimeters(size.Width) >= MinWidthMm && ToMillimeters(size.Height) >= MinHeightMm;
    }

    private static double ToMillimeters(double feet) {
        return UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);
    }
}
