using System;
using System.Collections.Generic;

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
        var box = GetSectionBox(intersection, frame) ?? GetUnionBoxInFrame(intersection, frame);
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

        double along = Round(box.Along + offset * 2, roundingMm, box.Along);
        double vertical = Round(box.Vertical + offset * 2, roundingMm, box.Vertical);

        // Габариты растут симметрично от центра, толщина остаётся как есть.
        var center = (box.Min + box.Max) / 2;
        var min = new XYZ(center.X - along / 2, box.Min.Y, center.Z - vertical / 2);
        var max = new XYZ(center.X + along / 2, box.Max.Y, center.Z + vertical / 2);
        return new OpeningBox(box.Frame, min, max);
    }

    /// <summary>
    /// Возвращает габариты прохода, измеренные по пересечению элемента с плитой в плоскости
    /// конструкции: границы конструкции сечение не обрезают.
    /// </summary>
    /// <remarks>
    /// Габариты самого пересечения для этого не годятся. Воздуховод, идущий у верха стены, пересекает
    /// её лишь частью сечения, и отверстие по такому пересечению вышло бы ниже воздуховода - в него
    /// не пролез бы тот, для кого его делают. Замер на модели: там, где Bim4Everyone ставил 750x500,
    /// по пересечению получалось 700x200.
    /// Плита продолжает конструкцию в её плоскости и оставляет исходной только толщину, поэтому
    /// наклонный проход по-прежнему даёт вытянутое сечение, как и должен.
    /// </remarks>
    private OpeningBox GetSectionBox(MepStructureIntersection intersection, StructureFrame frame) {
        var structureBox = GetStructureBoxInFrame(intersection, frame);
        if(structureBox is null) {
            return null;
        }

        var slab = CreateSlab(structureBox);
        if(slab is null) {
            return null;
        }

        OpeningBox union = null;
        var toFrame = frame.Transform.Inverse;
        foreach(var mepSolid in GeometryUtils.GetSolids(intersection.Mep.Element)) {
            Solid section;
            try {
                section = BooleanOperationsUtils.ExecuteBooleanOperation(
                    mepSolid, slab, BooleanOperationsType.Intersect);
            } catch(Autodesk.Revit.Exceptions.ApplicationException) {
                continue;
            }
            if(section is null || section.Volume <= 0) {
                continue;
            }

            var box = ToBoxInFrame(section, toFrame, frame);
            if(box is null) {
                continue;
            }
            union = union is null ? box : union.Union(box);
        }

        if(union is null) {
            return null;
        }

        // Толщину задаёт конструкция, а не элемент: за её пределами резать нечего.
        return new OpeningBox(
            frame.Transform,
            new XYZ(union.Min.X, structureBox.Min.Y, union.Min.Z),
            new XYZ(union.Max.X, structureBox.Max.Y, union.Max.Z));
    }

    /// <summary>
    /// Строит плиту, продолжающую конструкцию в её плоскости: толщина конструкции, а в плоскости
    /// заведомо больше любого инженерного элемента.
    /// </summary>
    private Solid CreateSlab(OpeningBox structureBox) {
        double margin = UnitUtils.ConvertToInternalUnits(20000, UnitTypeId.Millimeters);
        double thickness = structureBox.Across;
        if(thickness <= 0) {
            return null;
        }

        var frame = structureBox.Frame;
        var corners = new List<XYZ> {
            frame.OfPoint(new XYZ(structureBox.Min.X - margin, structureBox.Min.Y, structureBox.Min.Z - margin)),
            frame.OfPoint(new XYZ(structureBox.Max.X + margin, structureBox.Min.Y, structureBox.Min.Z - margin)),
            frame.OfPoint(new XYZ(structureBox.Max.X + margin, structureBox.Min.Y, structureBox.Max.Z + margin)),
            frame.OfPoint(new XYZ(structureBox.Min.X - margin, structureBox.Min.Y, structureBox.Max.Z + margin))
        };

        try {
            var lines = new List<Curve>();
            for(int i = 0; i < corners.Count; i++) {
                lines.Add(Line.CreateBound(corners[i], corners[(i + 1) % corners.Count]));
            }
            var loop = CurveLoop.Create(lines);
            return GeometryCreationUtilities.CreateExtrusionGeometry(
                [loop], frame.OfVector(XYZ.BasisY), thickness);
        } catch(Autodesk.Revit.Exceptions.ApplicationException) {
            return null;
        }
    }

    /// <summary>
    /// Возвращает габариты конструкции в её системе координат.
    /// </summary>
    private OpeningBox GetStructureBoxInFrame(MepStructureIntersection intersection, StructureFrame frame) {
        var toFrame = frame.Transform.Inverse;
        var transform = intersection.StructureProvider.MainTransform;
        OpeningBox union = null;

        foreach(var raw in GeometryUtils.GetSolids(intersection.StructureElement)) {
            Solid inProject;
            try {
                inProject = SolidUtils.CreateTransformed(raw, transform);
            } catch(Autodesk.Revit.Exceptions.ApplicationException) {
                continue;
            }
            var box = ToBoxInFrame(inProject, toFrame, frame);
            if(box is null) {
                continue;
            }
            union = union is null ? box : union.Union(box);
        }
        return union;
    }

    /// <summary>
    /// Возвращает габариты солида в системе координат конструкции.
    /// </summary>
    private OpeningBox ToBoxInFrame(Solid solid, Transform toFrame, StructureFrame frame) {
        Solid transformed;
        try {
            transformed = SolidUtils.CreateTransformed(solid, toFrame);
        } catch(Autodesk.Revit.Exceptions.ApplicationException) {
            return null;
        }

        var box = transformed.GetBoundingBox();
        var first = box.Transform.OfPoint(box.Min);
        var second = box.Transform.OfPoint(box.Max);
        return new OpeningBox(
            frame.Transform,
            new XYZ(Math.Min(first.X, second.X), Math.Min(first.Y, second.Y), Math.Min(first.Z, second.Z)),
            new XYZ(Math.Max(first.X, second.X), Math.Max(first.Y, second.Y), Math.Max(first.Z, second.Z)));
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
    /// Округляет размер до ближайшего кратного заданному числу миллиметров, но не ниже сечения
    /// прохода.
    /// </summary>
    /// <remarks>
    /// До ближайшего, а не вверх. У Bim4Everyone правило двойное: перпендикулярный проход он считает
    /// по номинальному сечению элемента и округляет до ближайшего, а вверх округляет только там, где
    /// габарит снят с геометрии - наклонные проходы, арматура, объединённые задания. Мы измеряем
    /// геометрию всегда, и округление вверх на измеренном значении даёт лишний шаг: труба Ø104.8
    /// в стене замеряется как 108 (тесселяция круга), требует 208, и вверх это 250 против их 200.
    /// Замер на 78 заданиях: до ближайшего совпало 49, вверх - 42, причём 29 отверстий выходили
    /// на шаг крупнее. Цена выбора - до половины шага недостающего зазора: труба Ø19 с зазором 50
    /// с каждой стороны требует 119 мм, а при шаге 50 получает 100.
    /// Оговорка про сечение нужна для чужих настроек: при зазоре меньше половины шага округление
    /// вниз могло бы сделать отверстие уже того, что через него проходит.
    /// </remarks>
    private double Round(double value, int roundingMm, double sectionSize) {
        if(roundingMm <= 0) {
            return value;
        }

        double step = UnitUtils.ConvertToInternalUnits(roundingMm, UnitTypeId.Millimeters);
        double rounded = Math.Round(value / step, MidpointRounding.AwayFromZero) * step;
        return rounded < sectionSize ? rounded + step : rounded;
    }
}
