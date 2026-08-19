using Autodesk.Revit.DB;

using Juru.RevitPlugins.Openings.Models.Configs;
using Juru.RevitPlugins.Openings.Models.Intersections;

namespace Juru.RevitPlugins.Openings.Models.Placement;

/// <summary>
/// Угол поворота задания на отверстие вокруг вертикальной оси.
/// </summary>
public static class OpeningAngle {
    /// <summary>
    /// Возвращает угол поворота в радианах: отверстие в стене разворачивается по её нормали,
    /// отверстие в перекрытии не поворачивается.
    /// </summary>
    public static double Get(MepStructureIntersection intersection) {
        if(intersection.StructureKind != StructureKind.Wall
            || intersection.StructureElement is not Wall wall) {
            return 0;
        }

        var normal = intersection.StructureProvider.MainTransform.OfVector(wall.Orientation);
        double angle = normal.AngleTo(XYZ.BasisY);
        return normal.X <= 0 ? angle : -angle;
    }
}
