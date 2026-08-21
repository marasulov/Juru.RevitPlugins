using System;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Openings.Models.Configs;
using Juru.RevitPlugins.Openings.Models.Intersections;

namespace Juru.RevitPlugins.Openings.Models.Placement;

/// <summary>
/// Точка вставки задания на отверстие.
/// </summary>
/// <remarks>
/// Семейства взяты из библиотеки Bim4Everyone, и точка вставки у них не одна и та же. Замеры
/// геометрии относительно начала координат семейства:
/// <list type="bullet">
/// <item>в стене - начало на ближней грани стены, солид уходит от него на всю глубину;
/// по вертикали круглое отверстие центрировано, прямоугольное растёт вверх от нуля;</item>
/// <item>в перекрытии - начало на верхней грани плиты, солид уходит вниз; в плане центрировано.</item>
/// </list>
/// Конвенции собраны здесь, чтобы не разъезжаться по расчётам: ошибка в любой из них смещает
/// отверстие на половину габарита, и заметно это станет только на чертеже.
/// </remarks>
public static class InsertionPoint {
    /// <summary>
    /// Возвращает точку вставки семейства по габаритам отверстия и конструкции, которую оно пробивает.
    /// </summary>
    /// <param name="size">Габариты отверстия.</param>
    /// <param name="intersection">Пересечение, из которого получено отверстие.</param>
    /// <param name="elevationRoundingMm">
    /// Округление отметки в мм. Ноль оставляет отметку как есть.
    /// </param>
    public static XYZ Get(OpeningSize size, MepStructureIntersection intersection, int elevationRoundingMm = 0) {
        if(size is null) { throw new ArgumentNullException(nameof(size)); }
        if(intersection is null) { throw new ArgumentNullException(nameof(intersection)); }

        switch(size.OpeningType) {
            case OpeningType.WallRound:
                return RoundElevation(
                    ShiftToWallFace(size.Center, size, intersection), elevationRoundingMm);
            case OpeningType.WallRectangle:
                return RoundElevation(
                    ShiftToWallFace(size.Center - XYZ.BasisZ * size.Height / 2, size, intersection),
                    elevationRoundingMm);
            case OpeningType.FloorRectangle:
            case OpeningType.FloorRound:
                // Отметка отверстия в перекрытии не округляется: она лежит на верхней грани плиты,
                // и сдвиг по вертикали поднял бы отверстие над плитой или утопил в неё.
                return size.Center + XYZ.BasisZ * size.Thickness / 2;
            default:
                throw new ArgumentOutOfRangeException(nameof(size));
        }
    }

    /// <summary>
    /// Округляет отметку точки вставки до ближайшего кратного заданному числу миллиметров.
    /// </summary>
    /// <remarks>
    /// Отметка отверстия попадает в спецификацию и на разрез, и там она должна быть числом, к которому
    /// можно поставить размер: отметка 3247.9 непригодна, хотя геометрически верна. Bim4Everyone
    /// округляет так же и в том же месте - в точке вставки, а не только в параметре, иначе записанная
    /// отметка расходилась бы с геометрией.
    /// </remarks>
    private static XYZ RoundElevation(XYZ point, int roundingMm) {
        if(roundingMm <= 0) {
            return point;
        }

        double step = UnitUtils.ConvertToInternalUnits(roundingMm, UnitTypeId.Millimeters);
        double rounded = Math.Round(point.Z / step, MidpointRounding.AwayFromZero) * step;
        return new XYZ(point.X, point.Y, rounded);
    }

    /// <summary>
    /// Переносит точку с середины толщины стены на её грань: начало координат стеновых семейств
    /// лежит на грани, и от него солид уходит на всю глубину отверстия.
    /// </summary>
    private static XYZ ShiftToWallFace(
        XYZ point,
        OpeningSize size,
        MepStructureIntersection intersection) {

        if(intersection.StructureElement is not Wall wall) {
            return point;
        }

        var normal = intersection.StructureProvider.MainTransform.OfVector(wall.Orientation).Normalize();
        return point - normal * size.Thickness / 2;
    }
}
