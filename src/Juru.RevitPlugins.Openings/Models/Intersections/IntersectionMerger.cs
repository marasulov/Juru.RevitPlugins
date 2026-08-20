using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Openings.Models.Configs;

namespace Juru.RevitPlugins.Openings.Models.Intersections;

/// <summary>
/// Склеивает пересечения одного инженерного элемента с конструкциями, идущими вплотную друг к другу,
/// в одно задание на отверстие.
/// </summary>
/// <remarks>
/// Многослойную конструкцию чертят и слоями одного типа стены, и отдельными стенами вплотную. Во
/// втором случае одна труба даёт столько пересечений, сколько слоёв, и без склейки в одной точке
/// встанет несколько заданий. Замер на рабочей модели: из 41 точки с дублями 40 приходились именно
/// на разные элементы-стены, поэтому склейка по хосту тут не спасает - нужна склейка по расстоянию,
/// как в <c>MultilayerOpeningsGroupsProvider</c> у Bim4Everyone.
/// </remarks>
public class IntersectionMerger {
    private readonly double _maxGap;
    private readonly double _lateralTolerance;

    /// <param name="config">Настройки: расстояние склейки берётся из них.</param>
    public IntersectionMerger(OpeningsConfig config) {
        if(config is null) { throw new ArgumentNullException(nameof(config)); }

        _maxGap = UnitUtils.ConvertToInternalUnits(config.UnitedTaskMaxGapMm, UnitTypeId.Millimeters);
        _lateralTolerance = UnitUtils.ConvertToInternalUnits(
            config.UnitedTaskLateralToleranceMm, UnitTypeId.Millimeters);
    }

    /// <summary>
    /// Возвращает пересечения, в которых близкие проходы одного элемента склеены в один.
    /// </summary>
    public IReadOnlyCollection<MepStructureIntersection> Merge(
        IReadOnlyCollection<MepStructureIntersection> intersections) {

        if(intersections is null) { throw new ArgumentNullException(nameof(intersections)); }

        List<MepStructureIntersection> result = [];
        foreach(var byElement in intersections.GroupBy(item => item.Mep.Element.Id)) {
            result.AddRange(MergeForElement(byElement.ToList()));
        }
        return result;
    }

    private IEnumerable<MepStructureIntersection> MergeForElement(
        List<MepStructureIntersection> intersections) {

        // Направление, вдоль которого проход может продолжаться в следующем слое: ось элемента.
        // У соединительной арматуры оси нет, и такие пересечения склеиваются только по расстоянию.
        var axis = MepGeometry.GetDirection(intersections[0].Mep.Element);

        var pending = new List<MepStructureIntersection>(intersections);
        while(pending.Count > 0) {
            var group = new List<MepStructureIntersection> { pending[0] };
            pending.RemoveAt(0);

            bool grown = true;
            while(grown) {
                grown = false;
                for(int i = pending.Count - 1; i >= 0; i--) {
                    if(group.Any(member => Adjacent(member, pending[i], axis))) {
                        group.Add(pending[i]);
                        pending.RemoveAt(i);
                        grown = true;
                    }
                }
            }

            yield return group.Count == 1 ? group[0] : MepStructureIntersection.Unite(group);
        }
    }

    /// <summary>
    /// Проверяет, что два прохода одного элемента идут вплотную: зазор между их габаритами вдоль оси
    /// не больше порога склейки, а поперёк оси они смещены не больше допуска.
    /// </summary>
    /// <remarks>
    /// Сравниваются промежутки, занятые проходами вдоль оси, а не расстояние между их центрами:
    /// у толстой стены центр прохода уходит далеко от границы, и склейка по центрам пропускала
    /// пары, между которыми на самом деле нет зазора.
    /// </remarks>
    private bool Adjacent(MepStructureIntersection first, MepStructureIntersection second, XYZ axis) {
        if(axis is null) {
            return WithinGap(first, second, XYZ.BasisX)
                && WithinGap(first, second, XYZ.BasisY)
                && WithinGap(first, second, XYZ.BasisZ);
        }

        if(!WithinGap(first, second, axis)) {
            return false;
        }

        var firstCenter = first.GetCenter();
        var secondCenter = second.GetCenter();
        if(firstCenter is null || secondCenter is null) {
            return false;
        }

        double lateral = (secondCenter - firstCenter).CrossProduct(axis).GetLength();
        return lateral <= _lateralTolerance;
    }

    /// <summary>
    /// Проверяет, что промежутки двух проходов вдоль направления перекрываются
    /// либо расходятся не больше чем на порог склейки.
    /// </summary>
    private bool WithinGap(MepStructureIntersection first, MepStructureIntersection second, XYZ direction) {
        if(!first.TryGetInterval(direction, out double firstMin, out double firstMax)
            || !second.TryGetInterval(direction, out double secondMin, out double secondMax)) {
            return false;
        }

        double gap = Math.Max(firstMin, secondMin) - Math.Min(firstMax, secondMax);
        return gap <= _maxGap;
    }
}
