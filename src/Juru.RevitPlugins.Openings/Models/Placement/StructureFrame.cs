using System;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Openings.Models.Configs;
using Juru.RevitPlugins.Openings.Models.Intersections;

namespace Juru.RevitPlugins.Openings.Models.Placement;

/// <summary>
/// Система координат конструкции: X вдоль конструкции, Y сквозь её толщину, Z - третья ось.
/// </summary>
/// <remarks>
/// Габариты отверстия считаются в этой системе, а не в координатах проекта. Тогда наклонный проход
/// не требует отдельной математики: габаритный бокс солида пересечения в системе стены сам даёт
/// вытянутый по наклону размер, который иначе пришлось бы считать проекциями и теоремой Пифагора.
/// У стены сквозная ось горизонтальна, у перекрытия - вертикальна, а весь расчёт габаритов, склейки
/// и точки вставки написан в терминах "вдоль", "сквозь" и "третья ось" и одинаков для обеих.
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
        // Ось Y системы конструкции всегда идёт сквозь неё, поэтому у перекрытия она вертикальна.
        // Оси проекта здесь не годятся: с ними толщина плиты попадала в высоту отверстия, размер
        // в плане - в глубину, а склейка сравнивала допуск по толщине с расстоянием в плане,
        // из-за чего задания в перекрытиях объединялись не с теми и не по тем габаритам.
        var transform = Transform.Identity;
        transform.BasisX = XYZ.BasisX;
        transform.BasisY = XYZ.BasisZ;
        // Правая тройка: X x Y = Z, поэтому третья ось смотрит против Y проекта.
        transform.BasisZ = XYZ.BasisX.CrossProduct(XYZ.BasisZ);
        transform.Origin = XYZ.Zero;
        return new StructureFrame(transform, StructureKind.Floor);
    }
}
