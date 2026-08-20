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
    public static XYZ Get(OpeningSize size, MepStructureIntersection intersection) {
        if(size is null) { throw new ArgumentNullException(nameof(size)); }
        if(intersection is null) { throw new ArgumentNullException(nameof(intersection)); }

        switch(size.OpeningType) {
            case OpeningType.WallRound:
                return ShiftToWallFace(size.Center, size, intersection);
            case OpeningType.WallRectangle:
                return ShiftToWallFace(size.Center - XYZ.BasisZ * size.Height / 2, size, intersection);
            case OpeningType.FloorRectangle:
            case OpeningType.FloorRound:
                return size.Center + XYZ.BasisZ * size.Thickness / 2;
            default:
                throw new ArgumentOutOfRangeException(nameof(size));
        }
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
