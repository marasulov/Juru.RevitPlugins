using System;
using System.Collections.Generic;

using Autodesk.Revit.DB;

namespace Juru.RevitPlugins.Openings.Models.Placement;

/// <summary>
/// Габариты отверстия в системе координат конструкции: X вдоль конструкции, Y сквозь её толщину,
/// Z - третья ось (у стены это вертикаль, у перекрытия - вторая ось плана).
/// </summary>
public class OpeningBox {
    public OpeningBox(Transform frame, XYZ min, XYZ max) {
        Frame = frame ?? throw new ArgumentNullException(nameof(frame));
        Min = min ?? throw new ArgumentNullException(nameof(min));
        Max = max ?? throw new ArgumentNullException(nameof(max));
    }

    /// <summary>
    /// Преобразование из системы конструкции в координаты активного файла.
    /// </summary>
    public Transform Frame { get; }

    /// <summary>
    /// Наименьший угол габаритов в системе конструкции.
    /// </summary>
    public XYZ Min { get; }

    /// <summary>
    /// Наибольший угол габаритов в системе конструкции.
    /// </summary>
    public XYZ Max { get; }

    /// <summary>
    /// Размер вдоль конструкции.
    /// </summary>
    public double Along => Max.X - Min.X;

    /// <summary>
    /// Размер по толщине конструкции.
    /// </summary>
    public double Across => Max.Y - Min.Y;

    /// <summary>
    /// Размер по третьей оси: у стены это высота, у перекрытия - второй размер в плане.
    /// </summary>
    public double Vertical => Max.Z - Min.Z;

    /// <summary>
    /// Центр габаритов в координатах активного файла.
    /// </summary>
    public XYZ Center => Frame.OfPoint((Min + Max) / 2);

    /// <summary>
    /// Точка в координатах активного файла по координатам в системе конструкции.
    /// </summary>
    public XYZ PointAt(double x, double y, double z) {
        return Frame.OfPoint(new XYZ(x, y, z));
    }

    /// <summary>
    /// Переносит габариты в систему координат другого бокса.
    /// </summary>
    /// <remarks>
    /// Нужно, чтобы сравнивать отверстия в соседних стенах: у каждой стены своя система координат,
    /// а сравнивать габариты можно только в общей.
    /// </remarks>
    public OpeningBox ToFrameOf(OpeningBox other) {
        var toOther = other.Frame.Inverse.Multiply(Frame);
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

        foreach(var corner in GetCorners()) {
            var moved = toOther.OfPoint(corner);
            minX = Math.Min(minX, moved.X);
            minY = Math.Min(minY, moved.Y);
            minZ = Math.Min(minZ, moved.Z);
            maxX = Math.Max(maxX, moved.X);
            maxY = Math.Max(maxY, moved.Y);
            maxZ = Math.Max(maxZ, moved.Z);
        }

        return new OpeningBox(other.Frame, new XYZ(minX, minY, minZ), new XYZ(maxX, maxY, maxZ));
    }

    /// <summary>
    /// Объединяет с другим боксом. Оба должны быть в одной системе координат.
    /// </summary>
    public OpeningBox Union(OpeningBox other) {
        return new OpeningBox(
            Frame,
            new XYZ(Math.Min(Min.X, other.Min.X), Math.Min(Min.Y, other.Min.Y), Math.Min(Min.Z, other.Min.Z)),
            new XYZ(Math.Max(Max.X, other.Max.X), Math.Max(Max.Y, other.Max.Y), Math.Max(Max.Z, other.Max.Z)));
    }

    /// <summary>
    /// Проверяет, соприкасаются ли габариты: перекрываются по всем трём осям либо расходятся
    /// не больше допуска.
    /// </summary>
    /// <param name="other">Другие габариты в этой же системе координат.</param>
    /// <param name="alongTolerance">Допустимый зазор вдоль конструкции и по вертикали.</param>
    /// <param name="acrossTolerance">Допустимый зазор по толщине конструкции.</param>
    public bool Touches(OpeningBox other, double alongTolerance, double acrossTolerance) {
        return Gap(Min.X, Max.X, other.Min.X, other.Max.X) <= alongTolerance
            && Gap(Min.Z, Max.Z, other.Min.Z, other.Max.Z) <= alongTolerance
            && Gap(Min.Y, Max.Y, other.Min.Y, other.Max.Y) <= acrossTolerance;
    }

    private static double Gap(double firstMin, double firstMax, double secondMin, double secondMax) {
        return Math.Max(firstMin, secondMin) - Math.Min(firstMax, secondMax);
    }

    private IEnumerable<XYZ> GetCorners() {
        double[] xs = [Min.X, Max.X];
        double[] ys = [Min.Y, Max.Y];
        double[] zs = [Min.Z, Max.Z];
        foreach(double x in xs) {
            foreach(double y in ys) {
                foreach(double z in zs) {
                    yield return new XYZ(x, y, z);
                }
            }
        }
    }
}
