using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Openings.Models.Configs;
using Juru.RevitPlugins.Openings.Models.Intersections;

namespace Juru.RevitPlugins.Openings.Models.Placement;

/// <summary>
/// Отверстие, которое предстоит разместить: его габариты и пересечения, из которых оно получено.
/// </summary>
public class PlannedOpening {
    private readonly List<MepStructureIntersection> _sources;

    public PlannedOpening(
        MepStructureIntersection source,
        OpeningBox box,
        OpeningShape shape,
        StructureKind structureKind) {

        _sources = [source ?? throw new ArgumentNullException(nameof(source))];
        Box = box ?? throw new ArgumentNullException(nameof(box));
        Shape = shape;
        StructureKind = structureKind;
    }

    private PlannedOpening(
        IEnumerable<MepStructureIntersection> sources,
        OpeningBox box,
        OpeningShape shape,
        StructureKind structureKind) {

        _sources = sources.ToList();
        Box = box;
        Shape = shape;
        StructureKind = structureKind;
    }

    /// <summary>
    /// Габариты отверстия в системе координат конструкции.
    /// </summary>
    public OpeningBox Box { get; }

    /// <summary>
    /// Форма отверстия.
    /// </summary>
    public OpeningShape Shape { get; }

    /// <summary>
    /// Вид конструкции.
    /// </summary>
    public StructureKind StructureKind { get; }

    /// <summary>
    /// Пересечения, из которых получено отверстие.
    /// </summary>
    public IReadOnlyCollection<MepStructureIntersection> Sources => _sources;

    /// <summary>
    /// Пересечение, по которому берутся хост, нормаль и настройки: самое крупное в группе.
    /// </summary>
    public MepStructureIntersection Primary => _sources
        .OrderByDescending(source => source.Volume)
        .First();

    /// <summary>
    /// Отверстие получено склейкой нескольких проходов.
    /// </summary>
    public bool IsUnited => _sources.Count > 1;

    /// <summary>
    /// Тип отверстия: конструкция и форма.
    /// </summary>
    public OpeningType OpeningType => StructureKind == StructureKind.Wall
        ? (Shape == OpeningShape.Round ? OpeningType.WallRound : OpeningType.WallRectangle)
        : (Shape == OpeningShape.Round ? OpeningType.FloorRound : OpeningType.FloorRectangle);

    /// <summary>
    /// Склеивает группу отверстий в одно: габариты объединяются в системе координат самого крупного,
    /// а форма остаётся круглой только если круглые все и их габариты совпадают в плане реза.
    /// </summary>
    public static PlannedOpening Unite(
        IReadOnlyCollection<PlannedOpening> group,
        double sameSizeTolerance,
        double roundingStep) {
        if(group is null || group.Count == 0) {
            throw new ArgumentException("Склеивать нечего.", nameof(group));
        }
        if(group.Count == 1) {
            return group.First();
        }

        var primary = group.OrderByDescending(item => item.Box.Along * item.Box.Vertical).First();
        var box = primary.Box;
        bool sameSection = true;
        foreach(var item in group.Where(item => item != primary)) {
            var moved = item.Box.ToFrameOf(primary.Box);
            sameSection = sameSection
                && Math.Abs(moved.Along - box.Along) <= sameSizeTolerance
                && Math.Abs(moved.Vertical - box.Vertical) <= sameSizeTolerance;
            box = box.Union(moved);
        }

        // Круглым объединённое остаётся только если это один и тот же проход через слои:
        // одинаковое сечение у всех членов группы. Иначе круг не накрыл бы то, что в него не входит.
        bool round = group.All(item => item.Shape == OpeningShape.Round) && sameSection;

        return new PlannedOpening(
            group.SelectMany(item => item.Sources),
            RoundUp(box, roundingStep),
            round ? OpeningShape.Round : OpeningShape.Rectangle,
            primary.StructureKind);
    }

    /// <summary>
    /// Округляет габариты объединённого отверстия вверх до заданного шага.
    /// </summary>
    /// <remarks>
    /// Зазор заново не добавляется - он уже заложен в каждом члене группы. Bim4Everyone поступает так же:
    /// у объединённых заданий свой шаг округления, обычно мельче основного, иначе объединение слоёв
    /// давало бы некруглые габариты вида 793 мм.
    /// </remarks>
    private static OpeningBox RoundUp(OpeningBox box, double step) {
        if(step <= 0) {
            return box;
        }

        double along = Math.Ceiling(Math.Round(box.Along / step, 5)) * step;
        double vertical = Math.Ceiling(Math.Round(box.Vertical / step, 5)) * step;
        var center = (box.Min + box.Max) / 2;
        return new OpeningBox(
            box.Frame,
            new XYZ(center.X - along / 2, box.Min.Y, center.Z - vertical / 2),
            new XYZ(center.X + along / 2, box.Max.Y, center.Z + vertical / 2));
    }

    /// <summary>
    /// Возвращает габариты отверстия для записи в параметры и расчёта точки вставки.
    /// </summary>
    public OpeningSize GetSize() {
        double diameter = Shape == OpeningShape.Round ? Math.Max(Box.Along, Box.Vertical) : 0;
        return new OpeningSize(
            OpeningType,
            Shape == OpeningShape.Round ? 0 : Box.Along,
            Shape == OpeningShape.Round ? 0 : Box.Vertical,
            diameter,
            Box.Across,
            Box.Center);
    }
}
