using Autodesk.Revit.DB;

using Juru.RevitPlugins.Openings.Models.Configs;

namespace Juru.RevitPlugins.Openings.Models.Intersections;

/// <summary>
/// Пересечение инженерного элемента активного файла с конструкцией из связи.
/// </summary>
public class MepStructureIntersection {
    public MepStructureIntersection(
        MepElementCandidate mep,
        StructureElementsProvider structureProvider,
        Element structureElement,
        Solid intersection) {

        Mep = mep;
        StructureProvider = structureProvider;
        StructureElement = structureElement;
        Intersection = intersection;
        StructureKind = StructureElementsProvider.GetKind(structureElement);
    }

    /// <summary>
    /// Инженерный элемент с настройками своего вида.
    /// </summary>
    public MepElementCandidate Mep { get; }

    /// <summary>
    /// Связь, из которой взята конструкция.
    /// </summary>
    public StructureElementsProvider StructureProvider { get; }

    /// <summary>
    /// Конструкция из связи.
    /// </summary>
    public Element StructureElement { get; }

    /// <summary>
    /// Вид конструкции.
    /// </summary>
    public StructureKind StructureKind { get; }

    /// <summary>
    /// Солид пересечения в координатах активного файла,
    /// либо <c>null</c>, если геометрию пересечения вычислить не удалось.
    /// </summary>
    public Solid Intersection { get; }

    /// <summary>
    /// Пересечение найдено, но его геометрия не вычислена: габариты отверстия по нему не построить.
    /// </summary>
    public bool GeometryIsInvalid => Intersection is null;

    /// <summary>
    /// Объём пересечения в единицах Revit, либо 0, если геометрия не вычислена.
    /// </summary>
    public double Volume => Intersection?.Volume ?? 0;

    /// <summary>
    /// Настройки вида инженерного элемента.
    /// </summary>
    public MepCategorySettings Settings => Mep.Settings;
}
