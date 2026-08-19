using System;

using Autodesk.Revit.DB;

namespace Juru.RevitPlugins.Openings.Models.Intersections;

/// <summary>
/// Ориентация и площадь сечения инженерных элементов.
/// </summary>
public static class MepGeometry {
    private const double _angleTolerance = 0.01;

    /// <summary>
    /// Возвращает направление осевой линии элемента, либо <c>null</c>,
    /// если у элемента нет осевой линии (соединительная арматура).
    /// </summary>
    public static XYZ GetDirection(Element element) {
        return element.Location is LocationCurve location && location.Curve is Line line
            ? line.Direction
            : null;
    }

    /// <summary>
    /// Проверяет, что осевая линия элемента вертикальна.
    /// </summary>
    public static bool IsVertical(Element element) {
        var direction = GetDirection(element);
        return direction is not null && Math.Abs(Math.Abs(direction.Z) - 1) < _angleTolerance;
    }

    /// <summary>
    /// Проверяет, что осевая линия элемента горизонтальна.
    /// </summary>
    public static bool IsHorizontal(Element element) {
        var direction = GetDirection(element);
        return direction is not null && Math.Abs(direction.Z) < _angleTolerance;
    }

    /// <summary>
    /// Возвращает площадь сечения элемента в единицах Revit.
    /// Используется как мера того, какой объём пересечения считать значимым.
    /// </summary>
    public static double GetSectionArea(MepElementSize size) {
        if(size is null) {
            return 0;
        }
        return size.IsRound
            ? Math.PI * size.Diameter * size.Diameter / 4
            : size.Width * size.Height;
    }
}
