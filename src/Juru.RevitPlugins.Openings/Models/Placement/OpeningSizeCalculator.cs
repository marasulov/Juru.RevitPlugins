using System;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Openings.Models.Configs;
using Juru.RevitPlugins.Openings.Models.Intersections;

namespace Juru.RevitPlugins.Openings.Models.Placement;

/// <summary>
/// Считает габариты задания на отверстие по солиду пересечения.
/// </summary>
public class OpeningSizeCalculator {
    /// <summary>
    /// Наименьший допустимый габарит отверстия в мм. Меньше - геометрия семейства не строится.
    /// </summary>
    private const double _minSizeMm = 5;

    /// <summary>
    /// Возвращает габариты задания, либо <c>null</c>, если габарит оказался недопустимо мал
    /// либо геометрию пересечения вычислить не удалось.
    /// </summary>
    public OpeningSize Calculate(MepStructureIntersection intersection) {
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

        // Зазор добавляется с двух сторон габарита.
        double alongStructure = box.Max.X - box.Min.X + offset * 2;
        double across = box.Max.Y - box.Min.Y;
        double vertical = box.Max.Z - box.Min.Z;

        double width;
        double height;
        double thickness;
        if(frame.Kind == StructureKind.Wall) {
            width = Round(alongStructure, settings.SizeRoundingMm);
            height = Round(vertical + offset * 2, settings.SizeRoundingMm);
            // Толщину не округляем: округлённая глубина не прорежет конструкцию насквозь.
            thickness = across;
        } else {
            width = Round(alongStructure, settings.SizeRoundingMm);
            height = Round(across + offset * 2, settings.SizeRoundingMm);
            thickness = vertical;
        }

        var shape = offsetRange?.Shape ?? OpeningShape.Rectangle;
        double diameter = 0;
        if(shape == OpeningShape.Round) {
            // Круглое отверстие должно накрыть оба габарита пересечения.
            diameter = Math.Max(width, height);
            width = 0;
            height = 0;
        }

        if(!SizeIsAllowed(shape == OpeningShape.Round ? diameter : Math.Min(width, height))) {
            return null;
        }

        var center = frame.Transform.OfPoint((box.Min + box.Max) / 2);
        return new OpeningSize(
            GetOpeningType(frame.Kind, shape),
            width,
            height,
            diameter,
            thickness,
            center);
    }

    /// <summary>
    /// Возвращает общий габаритный бокс всех солидов пересечения в системе координат конструкции:
    /// слои составной конструкции складываются в одно отверстие на всю её толщину.
    /// </summary>
    private BoundingBoxXYZ GetUnionBoxInFrame(MepStructureIntersection intersection, StructureFrame frame) {
        BoundingBoxXYZ union = null;
        foreach(var solid in intersection.Solids) {
            var box = GetBoxInFrame(solid, frame);
            if(box is null) {
                continue;
            }
            if(union is null) {
                union = box;
                continue;
            }
            union.Min = new XYZ(
                Math.Min(union.Min.X, box.Min.X),
                Math.Min(union.Min.Y, box.Min.Y),
                Math.Min(union.Min.Z, box.Min.Z));
            union.Max = new XYZ(
                Math.Max(union.Max.X, box.Max.X),
                Math.Max(union.Max.Y, box.Max.Y),
                Math.Max(union.Max.Z, box.Max.Z));
        }
        return union;
    }

    /// <summary>
    /// Возвращает габаритный бокс солида в системе координат конструкции.
    /// </summary>
    private BoundingBoxXYZ GetBoxInFrame(Solid solid, StructureFrame frame) {
        try {
            var toFrame = frame.Transform.Inverse;
            var transformed = SolidUtils.CreateTransformed(solid, toFrame);
            return transformed.GetBoundingBox() is { } box ? ToWorldAlignedBox(box) : null;
        } catch(Autodesk.Revit.Exceptions.ApplicationException) {
            return null;
        }
    }

    /// <summary>
    /// Приводит бокс солида к системе, в которой он был построен: <see cref="Solid.GetBoundingBox"/>
    /// возвращает бокс в собственных координатах солида вместе с их преобразованием.
    /// </summary>
    private BoundingBoxXYZ ToWorldAlignedBox(BoundingBoxXYZ box) {
        var transform = box.Transform;
        var first = transform.OfPoint(box.Min);
        var second = transform.OfPoint(box.Max);
        var result = new BoundingBoxXYZ {
            Min = new XYZ(Math.Min(first.X, second.X), Math.Min(first.Y, second.Y), Math.Min(first.Z, second.Z)),
            Max = new XYZ(Math.Max(first.X, second.X), Math.Max(first.Y, second.Y), Math.Max(first.Z, second.Z))
        };
        return result;
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

    private OpeningType GetOpeningType(StructureKind kind, OpeningShape shape) {
        if(kind == StructureKind.Wall) {
            return shape == OpeningShape.Round ? OpeningType.WallRound : OpeningType.WallRectangle;
        }
        return shape == OpeningShape.Round ? OpeningType.FloorRound : OpeningType.FloorRectangle;
    }
}
