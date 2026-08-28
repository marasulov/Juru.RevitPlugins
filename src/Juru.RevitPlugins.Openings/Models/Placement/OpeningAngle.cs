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

    /// <summary>
    /// Возвращает текущий поворот уже размещённого экземпляра вокруг вертикальной оси, в радианах -
    /// не пересчитывает его из геометрии конструкции, а читает из фактической трансформации
    /// экземпляра. Нужен там, где исходного пересечения уже нет: при отправке задания на сервер и
    /// при постановке входящего задания в файле АР/КР.
    /// </summary>
    public static double GetCurrent(Transform frame) {
        return XYZ.BasisX.AngleOnPlaneTo(frame.BasisX, XYZ.BasisZ);
    }
}
