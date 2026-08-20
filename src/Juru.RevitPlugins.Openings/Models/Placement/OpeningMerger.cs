using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Openings.Models.Configs;

namespace Juru.RevitPlugins.Openings.Models.Placement;

/// <summary>
/// Склеивает отверстия, которые соприкасаются или перекрываются, в одно.
/// </summary>
/// <remarks>
/// Так же поступает Bim4Everyone: он объединяет солиды двух заданий и смотрит, остался ли результат
/// одним объёмом (<c>SolidUtils.SplitVolumes(union).Count == 1</c>). Существенно, что сравниваются
/// габариты **отверстий**, уже расширенные на зазор, а не проходы: за счёт зазора соседние отверстия
/// перекрываются даже там, где сами конструкции слегка расходятся.
/// Группировка не смотрит, какой элемент породил отверстие: две параллельные трубы, чьи отверстия
/// наложились по ширине, дают одно отверстие - иначе в стене оказались бы два пересекающихся проёма.
/// </remarks>
public class OpeningMerger {
    private readonly double _touchTolerance;
    private readonly double _acrossTolerance;
    private readonly double _sameSizeTolerance;
    private readonly double _unitedRounding;

    public OpeningMerger(OpeningsConfig config) {
        if(config is null) { throw new ArgumentNullException(nameof(config)); }

        _touchTolerance = UnitUtils.ConvertToInternalUnits(
            config.UnitedTaskTouchToleranceMm, UnitTypeId.Millimeters);
        _acrossTolerance = UnitUtils.ConvertToInternalUnits(
            config.UnitedTaskMaxGapMm, UnitTypeId.Millimeters);
        _sameSizeTolerance = UnitUtils.ConvertToInternalUnits(
            config.UnitedTaskSizeRoundingMm, UnitTypeId.Millimeters);
        _unitedRounding = UnitUtils.ConvertToInternalUnits(
            config.UnitedTaskSizeRoundingMm, UnitTypeId.Millimeters);
    }

    /// <summary>
    /// Возвращает отверстия, в которых соприкасающиеся склеены в одно.
    /// </summary>
    public IReadOnlyCollection<PlannedOpening> Merge(IReadOnlyCollection<PlannedOpening> openings) {
        if(openings is null) { throw new ArgumentNullException(nameof(openings)); }

        List<PlannedOpening> result = [];

        // Отверстия в стенах и в перекрытиях не склеиваются между собой: у них разные семейства
        // и разные точки вставки.
        foreach(var byKind in openings.GroupBy(opening => opening.StructureKind)) {
            foreach(var group in BuildGroups(byKind.ToList())) {
                result.Add(PlannedOpening.Unite(group, _sameSizeTolerance, _unitedRounding));
            }
        }

        return result;
    }

    /// <summary>
    /// Собирает группы соприкасающихся отверстий. Группа растёт, пока к ней есть что присоединить:
    /// три слоя подряд попадают в одну группу, даже если первый и третий друг друга не касаются.
    /// </summary>
    private IEnumerable<List<PlannedOpening>> BuildGroups(List<PlannedOpening> openings) {
        var pending = new List<PlannedOpening>(openings);

        while(pending.Count > 0) {
            var group = new List<PlannedOpening> { pending[0] };
            pending.RemoveAt(0);

            bool grown = true;
            while(grown) {
                grown = false;
                for(int i = pending.Count - 1; i >= 0; i--) {
                    if(group.Any(member => Touches(member, pending[i]))) {
                        group.Add(pending[i]);
                        pending.RemoveAt(i);
                        grown = true;
                    }
                }
            }

            yield return group;
        }
    }

    private bool Touches(PlannedOpening first, PlannedOpening second) {
        var moved = second.Box.ToFrameOf(first.Box);
        return first.Box.Touches(moved, _touchTolerance, _acrossTolerance);
    }
}
