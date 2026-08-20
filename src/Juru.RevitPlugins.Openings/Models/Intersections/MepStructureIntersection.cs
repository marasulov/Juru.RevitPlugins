using System;
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

    /// <summary>
    /// Сколько проходов склеено в это пересечение. Единица означает обычное пересечение.
    /// </summary>
    public int MergedCount { get; private set; } = 1;

    /// <summary>
    /// Задание получено склейкой нескольких проходов вплотную идущих конструкций.
    /// </summary>
    public bool IsUnited => MergedCount > 1;

    /// <summary>
    /// Возвращает центр габаритов пересечения в координатах активного файла,
    /// либо <c>null</c>, если геометрии нет.
    /// </summary>
    public XYZ GetCenter() {
        if(_solids.Count == 0) {
            return null;
        }

        double weight = 0;
        var sum = XYZ.Zero;
        foreach(var solid in _solids) {
            var centroid = solid.ComputeCentroid();
            sum += centroid * solid.Volume;
            weight += solid.Volume;
        }
        return weight > 0 ? sum / weight : null;
    }

    /// <summary>
    /// Склеивает несколько пересечений одного элемента в одно. Основной конструкцией становится та,
    /// по которой пересечение крупнее: от неё берутся нормаль и система координат для габаритов.
    /// </summary>
    public static MepStructureIntersection Unite(IReadOnlyCollection<MepStructureIntersection> group) {
        if(group is null || group.Count == 0) {
            throw new ArgumentException("Склеивать нечего.", nameof(group));
        }

        var primary = group.OrderByDescending(item => item.Volume).First();
        var united = new MepStructureIntersection(
            primary.Mep,
            primary.StructureProvider,
            primary.StructureElement);

        foreach(var solid in group.SelectMany(item => item.Solids)) {
            united.AddSolid(solid);
        }
        united.MergedCount = group.Count;
        return united;
    }
}
