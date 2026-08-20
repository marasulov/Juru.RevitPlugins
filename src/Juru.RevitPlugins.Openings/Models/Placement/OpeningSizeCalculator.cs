using System;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Openings.Models.Configs;
using Juru.RevitPlugins.Openings.Models.Intersections;

namespace Juru.RevitPlugins.Openings.Models.Placement;

/// <summary>
/// Считает габариты отверстия по солидам пересечения.
/// </summary>
public class OpeningSizeCalculator {
    /// <summary>
    /// Наименьший допустимый габарит отверстия в мм. Меньше - геометрия семейства не строится.
    /// </summary>
    private const double _minSizeMm = 5;

    /// <summary>
    /// Возвращает планируемое отверстие, либо <c>null</c>, если габарит оказался недопустимо мал
    /// либо геометрию пересечения вычислить не удалось.
    /// </summary>
    public PlannedOpening Calculate(MepStructureIntersection intersection) {
        if(intersection is null) { throw new ArgumentNullException(nameof(intersection)); }
        if(intersection.GeometryIsInvalid) {
            return null;
        }

        var frame = StructureFrame.Create(intersection);
        var box = GetUnionBoxInFrame(intersection, frame);
        if(box is null) {
            return null;
        }

        var settings = intersection.Settings;
        double governingSizeMm = UnitUtils.ConvertFromInternalUnits(
            intersection.Mep.Size.GoverningSize, UnitTypeId.Millimeters);
        var offsetRange = settings.GetOffsetRange(governingSizeMm);
        double offset = offsetRange is null
            ? 0
            : UnitUtils.ConvertToInternalUnits(offsetRange.OffsetMm, UnitTypeId.Millimeters);
        var shape = offsetRange?.Shape ?? OpeningShape.Rectangle;

        var sized = ApplyOffsetAndRounding(box, offset, settings.SizeRoundingMm, frame.Kind);
        double smallest = frame.Kind == StructureKind.Wall
            ? Math.Min(sized.Along, sized.Vertical)
            : Math.Min(sized.Along, sized.Vertical);
        if(!SizeIsAllowed(smallest)) {
            return null;
        }

        return new PlannedOpening(intersection, sized, shape, frame.Kind);
    }

    /// <summary>
    /// Расширяет габариты на зазор в плоскости реза и округляет их вверх.
    /// По толщине конструкции габарит не меняется: округлённая глубина не прорежет её насквозь.
    /// </summary>
    private OpeningBox ApplyOffsetAndRounding(
        OpeningBox box,
        double offset,
        int roundingMm,
        StructureKind kind) {

        double along = Round(box.Along + offset * 2, roundingMm);
        double vertical = Round(box.Vertical + offset * 2, roundingMm);

        // Габариты растут симметрично от центра, толщина остаётся как есть.
        var center = (box.Min + box.Max) / 2;
        var min = new XYZ(center.X - along / 2, box.Min.Y, center.Z - vertical / 2);
        var max = new XYZ(center.X + along / 2, box.Max.Y, center.Z + vertical / 2);
        return new OpeningBox(box.Frame, min, max);
    }

    /// <summary>
    /// Возвращает общий габаритный бокс всех солидов пересечения в системе координат конструкции:
    /// слои составной конструкции складываются в одно отверстие на всю её толщину.
    /// </summary>
    private OpeningBox GetUnionBoxInFrame(MepStructureIntersection intersection, StructureFrame frame) {
        OpeningBox union = null;
        var toFrame = frame.Transform.Inverse;

        foreach(var solid in intersection.Solids) {
            Solid transformed;
            try {
                transformed = SolidUtils.CreateTransformed(solid, toFrame);
            } catch(Autodesk.Revit.Exceptions.ApplicationException) {
                continue;
            }

            var box = transformed.GetBoundingBox();
            if(box is null) {
                continue;
            }

            var first = box.Transform.OfPoint(box.Min);
            var second = box.Transform.OfPoint(box.Max);
            var current = new OpeningBox(
                frame.Transform,
                new XYZ(Math.Min(first.X, second.X), Math.Min(first.Y, second.Y), Math.Min(first.Z, second.Z)),
                new XYZ(Math.Max(first.X, second.X), Math.Max(first.Y, second.Y), Math.Max(first.Z, second.Z)));

            union = union is null ? current : union.Union(current);
        }

        return union;
    }

    private bool SizeIsAllowed(double size) {
        return UnitUtils.ConvertFromInternalUnits(size, UnitTypeId.Millimeters) >= _minSizeMm;
    }

    /// <summary>
    /// Округляет размер вверх до заданного числа миллиметров: отверстие не должно оказаться
    /// меньше того, что через него проходит.
    /// </summary>
    private double Round(double value, int roundingMm) {
        if(roundingMm <= 0) {
            return value;
        }

        double step = UnitUtils.ConvertToInternalUnits(roundingMm, UnitTypeId.Millimeters);
        return Math.Ceiling(Math.Round(value / step, 5, MidpointRounding.AwayFromZero)) * step;
    }
}
