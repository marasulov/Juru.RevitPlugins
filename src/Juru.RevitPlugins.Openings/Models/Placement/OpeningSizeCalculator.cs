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
    /// Допуск перпендикулярности прохода: косинус угла между осью трассы и осью сквозь конструкцию.
    /// </summary>
    /// <remarks>
    /// Bim4Everyone требует точной перпендикулярности (косинус меньше 0.0001), но у нас проверяется
    /// не сама ось, а её отклонение от нормали, и допуск в полградуса завышает сечение
    /// на сотые доли миллиметра - меньше, чем погрешность самого замера.
    /// </remarks>
    private const double _perpendicularCosine = 0.99996;

    /// <summary>
    /// Насколько замеренное сечение может превышать номинальное, чтобы считаться тем же сечением, мм.
    /// </summary>
    /// <remarks>
    /// Гранение круга даёт превышение в единицы миллиметров. Если замер больше номинала сильнее,
    /// значит через конструкцию идёт что-то помимо самого элемента - изоляция, фланец, - и номиналу
    /// верить нельзя.
    /// </remarks>
    private const double _nominalToleranceMm = 5;

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

        // Правило округления зависит от того, откуда взялся габарит. У перпендикулярного прохода
        // сечение известно точно - это сечение элемента, и оно округляется до ближайшего шага.
        // Замеренное сечение - величина снизу: наклонный проход, отвод или клапан могут оказаться
        // больше, чем показал габаритный бокс, поэтому такое округляется вверх. Так же разделяет
        // эти случаи Bim4Everyone.
        var sized = TryGetNominalSection(intersection, box, out double nominalAlong, out double nominalVertical)
            ? ApplyNominalSection(box, nominalAlong, nominalVertical, offset, settings.SizeRoundingMm)
            : ApplyOffsetAndRounding(box, offset, settings.SizeRoundingMm, frame.Kind);
        double smallest = frame.Kind == StructureKind.Wall
            ? Math.Min(sized.Along, sized.Vertical)
            : Math.Min(sized.Along, sized.Vertical);
        if(!SizeIsAllowed(smallest)) {
            return null;
        }

        return new PlannedOpening(intersection, sized, shape, frame.Kind);
    }

    /// <summary>
    /// Определяет, известно ли сечение прохода точно, и возвращает его в осях конструкции.
    /// </summary>
    /// <remarks>
    /// Точно оно известно у линейного элемента, идущего сквозь конструкцию по нормали: тогда сечение
    /// отверстия - это сечение самого элемента. Габаритный бокс для такого прохода чуть больше:
    /// солид круглой трубы гранёный, и Ø104.8 замеряется как 108, а лишние миллиметры при округлении
    /// вверх стоят целого шага. Номинал раскладывается по осям конструкции по замеру, а не по
    /// собственным осям элемента: воздуховод может быть повёрнут вокруг своей оси.
    /// </remarks>
    private bool TryGetNominalSection(
        MepStructureIntersection intersection,
        OpeningBox box,
        out double along,
        out double vertical) {

        along = 0;
        vertical = 0;
        if(intersection.Mep.IsFitting || intersection.Mep.Element is not MEPCurve curve) {
            return false;
        }
        if(curve.Location is not LocationCurve location || location.Curve is not Line line) {
            return false;
        }

        var through = box.Frame.BasisY;
        if(Math.Abs(line.Direction.Normalize().DotProduct(through)) < _perpendicularCosine) {
            return false;
        }

        var size = intersection.Mep.Size;
        double first = size.IsRound ? size.Diameter : Math.Max(size.Width, size.Height);
        double second = size.IsRound ? size.Diameter : Math.Min(size.Width, size.Height);
        if(first <= 0 || second <= 0) {
            return false;
        }

        // Больший номинальный габарит ложится на ту ось, по которой замер больше.
        if(box.Along >= box.Vertical) {
            along = first;
            vertical = second;
        } else {
            along = second;
            vertical = first;
        }

        // Замер, заметно превышающий номинал, означает, что сквозь конструкцию идёт не только сам
        // элемент: изоляция, фланец. Тогда номинал занизил бы отверстие.
        double tolerance = UnitUtils.ConvertToInternalUnits(_nominalToleranceMm, UnitTypeId.Millimeters);
        return box.Along - along <= tolerance && box.Vertical - vertical <= tolerance;
    }

    /// <summary>
    /// Строит габариты отверстия по точно известному сечению прохода, округляя до ближайшего шага.
    /// </summary>
    private OpeningBox ApplyNominalSection(
        OpeningBox box,
        double nominalAlong,
        double nominalVertical,
        double offset,
        int roundingMm) {

        double along = RoundToNearest(nominalAlong + offset * 2, roundingMm, nominalAlong);
        double vertical = RoundToNearest(nominalVertical + offset * 2, roundingMm, nominalVertical);
        return Resize(box, along, vertical);
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

        double along = RoundUp(box.Along + offset * 2, roundingMm);
        double vertical = RoundUp(box.Vertical + offset * 2, roundingMm);
        return Resize(box, along, vertical);
    }

    /// <summary>
    /// Возвращает габариты с новыми размерами в плоскости реза: они растут симметрично от центра,
    /// толщина остаётся как есть.
    /// </summary>
    private OpeningBox Resize(OpeningBox box, double along, double vertical) {
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
    /// Округляет размер до ближайшего кратного заданному числу миллиметров, но не ниже сечения прохода.
    /// </summary>
    /// <remarks>
    /// До ближайшего округается только точно известное сечение - так делает Bim4Everyone для
    /// перпендикулярных проходов. Цена правила: до половины шага недостающего зазора - труба Ø19
    /// с зазором 50 с каждой стороны требует 119 мм, а при шаге 50 получает 100. Оговорка про
    /// сечение защищает от чужих настроек: при зазоре меньше половины шага округление вниз могло бы
    /// сделать отверстие уже того, что через него проходит.
    /// </remarks>
    private double RoundToNearest(double value, int roundingMm, double sectionSize) {
        if(roundingMm <= 0) {
            return value;
        }

        double step = UnitUtils.ConvertToInternalUnits(roundingMm, UnitTypeId.Millimeters);
        double rounded = Math.Round(value / step, MidpointRounding.AwayFromZero) * step;
        return rounded < sectionSize ? rounded + step : rounded;
    }

    /// <summary>
    /// Округляет размер вверх до кратного заданному числу миллиметров.
    /// </summary>
    /// <remarks>
    /// Вверх округляется замеренный габарит: замер - величина снизу, и отнимать у него ещё и часть
    /// зазора нельзя. Деление предварительно округляется до пяти знаков, иначе размер, уже кратный
    /// шагу, из-за погрешности перевода в футы вырастал бы на целый шаг.
    /// </remarks>
    private double RoundUp(double value, int roundingMm) {
        if(roundingMm <= 0) {
            return value;
        }

        double step = UnitUtils.ConvertToInternalUnits(roundingMm, UnitTypeId.Millimeters);
        return Math.Ceiling(Math.Round(value / step, 5)) * step;
    }
}
