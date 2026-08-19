using System;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Openings.Models.Configs;
using Juru.RevitPlugins.Openings.Models.Intersections;

namespace Juru.RevitPlugins.Openings.Models.Placement;

/// <summary>
/// Система координат конструкции: X вдоль конструкции, Y по её толщине, Z вверх.
/// </summary>
/// <remarks>
/// Габариты отверстия считаются в этой системе, а не в координатах проекта. Тогда наклонный проход
/// не требует отдельной математики: габаритный бокс солида пересечения в системе стены сам даёт
/// вытянутый по наклону размер, который иначе пришлось бы считать проекциями и теоремой Пифагора.
/// </remarks>
public class StructureFrame {
    private StructureFrame(Transform transform, StructureKind kind) {
        Transform = transform;
        Kind = kind;
    }

    /// <summary>
    /// Преобразование из системы конструкции в координаты активного файла.
    /// </summary>
    public Transform Transform { get; }

    /// <summary>
    /// Вид конструкции.
    /// </summary>
    public StructureKind Kind { get; }

    /// <summary>
    /// Строит систему координат по конструкции пересечения.
    /// </summary>
    public static StructureFrame Create(MepStructureIntersection intersection) {
        if(intersection is null) { throw new ArgumentNullException(nameof(intersection)); }

        return intersection.StructureKind == StructureKind.Wall
            ? CreateForWall(intersection)
            : CreateForFloor();
    }

    private static StructureFrame CreateForWall(MepStructureIntersection intersection) {
        var linkTransform = intersection.StructureProvider.MainTransform;
        var normal = linkTransform.OfVector(((Wall) intersection.StructureElement).Orientation).Normalize();
        // Ось X ведём вдоль стены в горизонтальной плоскости, чтобы ширина отверстия считалась
        // по стене, а высота строго по вертикали.
        var along = XYZ.BasisZ.CrossProduct(normal).Normalize();

        var transform = Transform.Identity;
        transform.BasisX = along;
        transform.BasisY = normal;
        transform.BasisZ = XYZ.BasisZ;
        transform.Origin = XYZ.Zero;
        return new StructureFrame(transform, StructureKind.Wall);
    }

    private static StructureFrame CreateForFloor() {
        // Для перекрытия оси проекта уже подходят: толщина идёт по Z.
        return new StructureFrame(Transform.Identity, StructureKind.Floor);
    }
}
