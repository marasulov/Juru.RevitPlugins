using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Openings.Models.Configs;

namespace Juru.RevitPlugins.Openings.Models.Intersections;

/// <summary>
/// Пересечение инженерного элемента активного файла с конструкцией из связи.
/// </summary>
public class MepStructureIntersection {
    private readonly List<Solid> _solids = [];

    public MepStructureIntersection(
        MepElementCandidate mep,
        StructureElementsProvider structureProvider,
        Element structureElement) {

        Mep = mep;
        StructureProvider = structureProvider;
        StructureElement = structureElement;
        StructureKind = StructureElementsProvider.GetKind(structureElement);
    }

    /// <summary>
    /// Добавляет солид пересечения. Составная конструкция отдаёт солид на слой, и труба, прошедшая
    /// сквозь штукатурку, кладку и штукатурку, даёт три солида на один и тот же проход - это одно
    /// отверстие, а не три.
    /// </summary>
    public void AddSolid(Solid solid) {
        if(solid is not null) {
            _solids.Add(solid);
        }
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
    /// Солиды пересечения в координатах активного файла - по одному на слой конструкции.
    /// </summary>
    public IReadOnlyCollection<Solid> Solids => _solids;

    /// <summary>
    /// Пересечение найдено, но его геометрия не вычислена: габариты отверстия по нему не построить.
    /// </summary>
    public bool GeometryIsInvalid => _solids.Count == 0;

    /// <summary>
    /// Суммарный объём пересечения в единицах Revit, либо 0, если геометрия не вычислена.
    /// </summary>
    public double Volume => _solids.Sum(solid => solid.Volume);

    /// <summary>
    /// Настройки вида инженерного элемента.
    /// </summary>
    public MepCategorySettings Settings => Mep.Settings;
}
