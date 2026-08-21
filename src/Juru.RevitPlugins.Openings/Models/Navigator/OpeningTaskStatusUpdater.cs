using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Openings.Models.Configs;
using Juru.RevitPlugins.Openings.Models.Intersections;

namespace Juru.RevitPlugins.Openings.Models.Navigator;

/// <summary>
/// Пересчитывает состояние заданий на отверстия по геометрии активного файла и связей.
/// </summary>
/// <remarks>
/// Проверки идут по очереди, и первая сработавшая назначает состояние: у задания, которое и висит
/// вне конструкций, и мало по габаритам, показывать нужно первую беду, а не последнюю. Порядок такой
/// же, как в плагине Bim4Everyone, на котором сверялась расстановка, - иначе один и тот же файл
/// два плагина описывали бы по-разному.
/// </remarks>
public class OpeningTaskStatusUpdater {
    /// <summary>
    /// Наименьшая сторона куба в мм, объём которого считается наименьшим годным объёмом задания.
    /// </summary>
    private const double _minVolumeSideMm = 10;

    /// <summary>
    /// Глубина проникновения элемента в конструкцию в мм, которая ещё считается погрешностью.
    /// </summary>
    private const double _overshootDepthMm = 2;

    private readonly Document _doc;
    private readonly MepElementsIndex _mepElements;
    private readonly IReadOnlyCollection<StructureElementsProvider> _structureProviders;
    private readonly ICollection<ElementId> _taskIds;
    private readonly double _minVolume;

    /// <summary>Кандидаты в хосты и связь, из которой они взяты, для обрабатываемого задания.</summary>
    private (IReadOnlyCollection<ElementId> Candidates, StructureElementsProvider? Link) _hosts;

    /// <param name="doc">Активный документ инженерных систем.</param>
    /// <param name="mepElements">Инженерные элементы активного файла.</param>
    /// <param name="structureProviders">Связи с конструкциями.</param>
    /// <param name="tasks">Все задания активного файла: по ним ищутся пересечения заданий между собой.</param>
    public OpeningTaskStatusUpdater(
        Document doc,
        MepElementsIndex mepElements,
        IReadOnlyCollection<StructureElementsProvider> structureProviders,
        IReadOnlyCollection<OpeningTask> tasks) {

        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _mepElements = mepElements ?? throw new ArgumentNullException(nameof(mepElements));
        _structureProviders = structureProviders ?? throw new ArgumentNullException(nameof(structureProviders));
        _taskIds = (tasks ?? throw new ArgumentNullException(nameof(tasks)))
            .Select(task => task.Id)
            .ToList();
        double side = UnitUtils.ConvertToInternalUnits(_minVolumeSideMm, UnitTypeId.Millimeters);
        _minVolume = side * side * side;
    }

    /// <summary>
    /// Назначает заданию состояние и находит его конструкцию-основу.
    /// </summary>
    public void Update(OpeningTask task) {
        if(task is null) { throw new ArgumentNullException(nameof(task)); }

        _hosts = ([], null);

        // Нестандартное отверстие плагин не считает: у него нет ни расчётных габаритов, ни зазоров.
        if(task.Status == OpeningTaskStatus.Unique) {
            FindAndSetHost(task);
            return;
        }

        try {
            task.MepElements = GetIntersectingMepElements(task);

            if(GeometryIsInvalid(task)) {
                task.Status = OpeningTaskStatus.Invalid;
                return;
            }
            if(task.IsManuallyPlaced) {
                task.Status = OpeningTaskStatus.ManuallyPlaced;
                return;
            }
            if(IsNotActual(task)) {
                task.Status = OpeningTaskStatus.NotActual;
                return;
            }
            if(IsInDifferentConstructions(task)) {
                task.Status = OpeningTaskStatus.DifferentConstructions;
                return;
            }
            if(IntersectsOtherTask(task)) {
                task.Status = OpeningTaskStatus.Intersects;
                return;
            }
            if(IsUnited(task)) {
                task.Status = OpeningTaskStatus.United;
                return;
            }

            task.Status = GetSizeStatus(task);
        } catch(Exception exception) when(
            exception is NullReferenceException
            or ArgumentException
            or InvalidOperationException
            or Autodesk.Revit.Exceptions.ApplicationException) {
            // Геометрия, на которой Revit спотыкается, не должна ронять навигатор целиком:
            // такое задание получает состояние "ошибка обработки" и остаётся в списке.
            task.Status = OpeningTaskStatus.Invalid;
        } finally {
            FindAndSetHost(task);
        }
    }

    private bool GeometryIsInvalid(OpeningTask task) {
        var solid = task.GetSolid();
        return solid is null || solid.Volume < _minVolume || task.Location is null;
    }

    /// <summary>
    /// Задание объединённое: либо так записано при расстановке, либо через него проходит
    /// больше одного инженерного элемента.
    /// </summary>
    private bool IsUnited(OpeningTask task) {
        return task.IsUnited || task.MepElements.Count > 1;
    }

    /// <summary>
    /// Проверяет, что задание больше не описывает существующий проход.
    /// </summary>
    /// <remarks>
    /// Двух причин достаточно: через задание не проходит ни один инженерный элемент, либо элемент
    /// через него проходит, но выходит за его габариты и пробивает конструкцию рядом - то есть
    /// отверстие перестало накрывать трубу. Проверяется это вычитанием солида задания из солида
    /// элемента, а не сравнением габаритов: сдвинутая на 10 см труба сохраняет габарит,
    /// но лезет в стену мимо отверстия.
    /// Задание, не попавшее ни в одну конструкцию, тоже неактуально: стену под ним удалили.
    /// </remarks>
    private bool IsNotActual(OpeningTask task) {
        if(task.MepElements.Count == 0) {
            return true;
        }

        foreach(var link in _structureProviders) {
            var candidates = GetIntersectingConstructions(task, link);
            if(candidates.Count == 0) {
                continue;
            }

            _hosts = (candidates, link);
            return MepElementsLeaveTask(task, candidates, link);
        }

        return true;
    }

    /// <summary>
    /// Проверяет, пробивают ли проходящие элементы конструкцию вне габаритов задания.
    /// </summary>
    private bool MepElementsLeaveTask(
        OpeningTask task,
        IReadOnlyCollection<ElementId> constructions,
        StructureElementsProvider link) {

        var mepSolid = GeometryUtils.Unite(task.MepElements.SelectMany(GeometryUtils.GetSolids));
        var taskSolid = task.GetSolid();
        if(mepSolid is null || taskSolid is null) {
            return false;
        }

        Solid outsideTask;
        try {
            var toLink = link.MainTransform.Inverse;
            outsideTask = BooleanOperationsUtils.ExecuteBooleanOperation(
                SolidUtils.CreateTransformed(mepSolid, toLink),
                SolidUtils.CreateTransformed(taskSolid, toLink),
                BooleanOperationsType.Difference);
        } catch(Autodesk.Revit.Exceptions.ApplicationException) {
            return false;
        }

        if(outsideTask is null || outsideTask.Volume <= 0) {
            return false;
        }

        // Чистовые отверстия из связи здесь не учитываются: их расстановку плагин пока не делает,
        // и в связях лежат только конструкции. Когда появится сторона АР, элемент, прошедший через
        // уже вырезанное отверстие, придётся считать не вылезшим за задание, а прошедшим сквозь него.
        var suspects = new FilteredElementCollector(link.Doc, constructions.ToList())
            .WherePasses(new BoundingBoxIntersectsFilter(GeometryUtils.GetOutline(outsideTask)))
            .WherePasses(new ElementIntersectsSolidFilter(outsideTask))
            .ToElements();
        if(suspects.Count == 0) {
            return false;
        }

        return GetIntersectionVolume(outsideTask, suspects) > GetOvershootTolerance(task);
    }

    /// <summary>
    /// Возвращает суммарный объём пересечения солида с конструкциями.
    /// </summary>
    private double GetIntersectionVolume(Solid solid, IEnumerable<Element> elements) {
        double volume = 0;
        foreach(var element in elements) {
            var structureSolid = GeometryUtils.Unite(GeometryUtils.GetSolids(element));
            if(structureSolid is null) {
                continue;
            }

            try {
                volume += BooleanOperationsUtils
                    .ExecuteBooleanOperation(solid, structureSolid, BooleanOperationsType.Intersect)
                    ?.Volume ?? 0;
            } catch(Autodesk.Revit.Exceptions.ApplicationException) {
                // Геометрия не поддалась вычитанию: этот кандидат в счёт не идёт.
            }
        }
        return volume;
    }

    /// <summary>
    /// Возвращает объём, до которого выход элемента за габариты задания считается погрешностью.
    /// </summary>
    /// <remarks>
    /// Глубина задания равна толщине конструкции, поэтому грани задания и конструкции совпадают,
    /// и на совпадающих гранях вычитание солидов оставляет тонкую плёнку. Без допуска такая плёнка
    /// объявляла бы неактуальным каждое второе корректное задание. Порог задан как проникновение
    /// на пару миллиметров по всему сечению задания: настоящий промах трубы мимо отверстия
    /// даёт объём на порядки больше.
    /// </remarks>
    private double GetOvershootTolerance(OpeningTask task) {
        var (width, height, diameter, _) = task.GetSize();
        double area = task.IsRound ? diameter * diameter : width * height;
        double depth = UnitUtils.ConvertToInternalUnits(_overshootDepthMm, UnitTypeId.Millimeters);
        return Math.Max(area * depth, _minVolume);
    }

    /// <summary>
    /// Проверяет, попало ли задание сразу в стену и в перекрытие.
    /// </summary>
    private bool IsInDifferentConstructions(OpeningTask task) {
        var (candidates, link) = GetHostCandidates(task);
        if(link is null || candidates.Count == 0) {
            return false;
        }

        return candidates
            .Select(id => StructureElementsProvider.GetKind(link.Doc.GetElement(id)))
            .Distinct()
            .Count() > 1;
    }

    private bool IntersectsOtherTask(OpeningTask task) {
        var solid = task.GetSolid();
        if(solid is null || _taskIds.Count < 2) {
            return false;
        }

        return new FilteredElementCollector(_doc, _taskIds.ToList())
            .Excluding(new List<ElementId> { task.Id })
            .WherePasses(new BoundingBoxIntersectsFilter(GeometryUtils.GetOutline(solid)))
            .WherePasses(new ElementIntersectsSolidFilter(solid))
            .Any();
    }

    /// <summary>
    /// Сравнивает зазор вокруг проходящего элемента с зазором из настроек его вида.
    /// </summary>
    /// <remarks>
    /// Замеряется не габарит задания, а именно зазор: сколько свободного места осталось вокруг
    /// элемента в плоскости реза. Габарит сам по себе ни о чём не говорит - отверстие 300x300
    /// велико для трубы Ø50 и мало для воздуховода 400x200.
    /// Допуск равен шагу округления габаритов: расстановка округляет размер до ближайшего кратного,
    /// поэтому зазор законно отличается от требуемого на этот шаг в обе стороны.
    /// </remarks>
    private OpeningTaskStatus GetSizeStatus(OpeningTask task) {
        var candidate = _mepElements.Get(task.MepElements.First().Id);
        if(candidate?.Settings is null) {
            // Вид элемента в настройках отсутствует: требуемый зазор взять негде, судить не о чем.
            return OpeningTaskStatus.Correct;
        }

        var clearance = GetClearance(task, candidate.Element);
        if(clearance is null) {
            return OpeningTaskStatus.Invalid;
        }

        double governingSizeMm = UnitUtils.ConvertFromInternalUnits(
            candidate.Size.GoverningSize, UnitTypeId.Millimeters);
        var range = candidate.Settings.GetOffsetRange(governingSizeMm);
        double required = range is null
            ? 0
            : UnitUtils.ConvertToInternalUnits(range.OffsetMm * 2, UnitTypeId.Millimeters);
        double tolerance = UnitUtils.ConvertToInternalUnits(
            Math.Max(candidate.Settings.SizeRoundingMm, 1), UnitTypeId.Millimeters);

        double minimum = Math.Max(required - tolerance, 0);
        double maximum = required + tolerance;
        var (along, vertical) = clearance.Value;

        if(along < minimum || vertical < minimum) {
            return OpeningTaskStatus.TooSmall;
        }
        if(along > maximum || vertical > maximum) {
            return OpeningTaskStatus.TooBig;
        }
        return OpeningTaskStatus.Correct;
    }

    /// <summary>
    /// Возвращает суммарный зазор с двух сторон элемента по каждой из осей плоскости реза,
    /// либо <c>null</c>, если геометрию сравнить не удалось.
    /// </summary>
    /// <remarks>
    /// Измеряется в системе координат задания: у стенового семейства это ось вдоль стены и вертикаль,
    /// у семейства в перекрытии - две оси в плане. Ось, идущая сквозь конструкцию, в замер не входит:
    /// по ней отверстие всегда равно толщине конструкции.
    /// </remarks>
    private (double Along, double Vertical)? GetClearance(OpeningTask task, Element mepElement) {
        var taskSolid = task.GetSolid();
        var mepSolid = GeometryUtils.Unite(GeometryUtils.GetSolids(mepElement));
        if(taskSolid is null || mepSolid is null) {
            return null;
        }

        var toFrame = task.Frame.Inverse;
        var taskBox = GetBoxInFrame(taskSolid, toFrame);
        var mepBox = GetBoxInFrame(mepSolid, toFrame);
        if(taskBox is null || mepBox is null) {
            return null;
        }

        // Ось сквозь конструкцию: у стенового семейства это его собственная Y, у семейства
        // в перекрытии - Z. Обмеряются две оставшиеся.
        return task.IsInWall
            ? (GetGapSum(taskBox, mepBox, axis: 0), GetGapSum(taskBox, mepBox, axis: 2))
            : (GetGapSum(taskBox, mepBox, axis: 0), GetGapSum(taskBox, mepBox, axis: 1));
    }

    /// <summary>
    /// Возвращает сумму зазоров между габаритами задания и элемента по одной оси.
    /// Отрицательное значение означает, что элемент вышел за габарит задания.
    /// </summary>
    private double GetGapSum(BoundingBoxXYZ task, BoundingBoxXYZ mep, int axis) {
        double taskMin = GetCoordinate(task.Min, axis);
        double taskMax = GetCoordinate(task.Max, axis);
        double mepMin = GetCoordinate(mep.Min, axis);
        double mepMax = GetCoordinate(mep.Max, axis);
        return (mepMin - taskMin) + (taskMax - mepMax);
    }

    private static double GetCoordinate(XYZ point, int axis) {
        return axis switch {
            0 => point.X,
            1 => point.Y,
            _ => point.Z
        };
    }

    /// <summary>
    /// Возвращает габариты солида в заданной системе координат.
    /// </summary>
    private BoundingBoxXYZ? GetBoxInFrame(Solid solid, Transform toFrame) {
        Solid transformed;
        try {
            transformed = SolidUtils.CreateTransformed(solid, toFrame);
        } catch(Autodesk.Revit.Exceptions.ApplicationException) {
            return null;
        }

        var box = transformed.GetBoundingBox();
        if(box is null) {
            return null;
        }

        var first = box.Transform.OfPoint(box.Min);
        var second = box.Transform.OfPoint(box.Max);
        return new BoundingBoxXYZ {
            Min = new XYZ(Math.Min(first.X, second.X), Math.Min(first.Y, second.Y), Math.Min(first.Z, second.Z)),
            Max = new XYZ(Math.Max(first.X, second.X), Math.Max(first.Y, second.Y), Math.Max(first.Z, second.Z))
        };
    }

    private IReadOnlyCollection<Element> GetIntersectingMepElements(OpeningTask task) {
        var solid = task.GetSolid();
        if(solid is null || _mepElements.Ids.Count == 0) {
            return [];
        }

        return new FilteredElementCollector(_doc, _mepElements.Ids.ToList())
            .WherePasses(new BoundingBoxIntersectsFilter(GeometryUtils.GetOutline(solid)))
            .WherePasses(new ElementIntersectsSolidFilter(solid))
            .ToElements()
            .ToList();
    }

    /// <summary>
    /// Возвращает конструкции из связи, с которыми пересекается задание.
    /// Их может быть несколько: составная конструкция начерчена отдельными стенами на слой.
    /// </summary>
    private IReadOnlyCollection<ElementId> GetIntersectingConstructions(
        OpeningTask task,
        StructureElementsProvider link) {

        var elements = link.GetElements();
        if(elements.Count == 0) {
            return [];
        }

        var solid = task.GetSolid();
        if(solid is null) {
            return [];
        }

        Solid inLink;
        try {
            inLink = SolidUtils.CreateTransformed(solid, link.MainTransform.Inverse);
        } catch(Autodesk.Revit.Exceptions.ApplicationException) {
            return [];
        }

        return new FilteredElementCollector(link.Doc, elements.Select(element => element.Id).ToList())
            .WherePasses(new BoundingBoxIntersectsFilter(GeometryUtils.GetOutline(inLink)))
            .WherePasses(new ElementIntersectsSolidFilter(inLink))
            .ToElementIds()
            .ToList();
    }

    private (IReadOnlyCollection<ElementId> Candidates, StructureElementsProvider? Link) GetHostCandidates(
        OpeningTask task) {

        if(_hosts.Link is not null) {
            return _hosts;
        }

        foreach(var link in _structureProviders) {
            var candidates = GetIntersectingConstructions(task, link);
            if(candidates.Count > 0) {
                _hosts = (candidates, link);
                return _hosts;
            }
        }

        return (Array.Empty<ElementId>(), null);
    }

    /// <summary>
    /// Назначает заданию конструкцию-основу: ту, с которой пересечение по объёму наибольшее.
    /// </summary>
    /// <remarks>
    /// Основа нужна навигатору, чтобы подсветить, что именно задание пробивает. У задания в составной
    /// конструкции кандидатов несколько, и показывать надо не первый попавшийся слой штукатурки,
    /// а несущую стену.
    /// </remarks>
    private void FindAndSetHost(OpeningTask task) {
        var (candidates, link) = GetHostCandidates(task);
        if(link is null || candidates.Count == 0) {
            return;
        }

        var solid = task.GetSolid();
        if(solid is null) {
            task.Host = link.Doc.GetElement(candidates.First());
            return;
        }

        Solid inLink;
        try {
            inLink = SolidUtils.CreateTransformed(solid, link.MainTransform.Inverse);
        } catch(Autodesk.Revit.Exceptions.ApplicationException) {
            task.Host = link.Doc.GetElement(candidates.First());
            return;
        }

        double half = inLink.Volume / 2;
        double best = 0;
        var host = link.Doc.GetElement(candidates.First());

        foreach(var id in candidates) {
            var element = link.Doc.GetElement(id);
            var structureSolid = GeometryUtils.Unite(GeometryUtils.GetSolids(element));
            if(structureSolid is null) {
                continue;
            }

            double volume;
            try {
                volume = BooleanOperationsUtils
                    .ExecuteBooleanOperation(inLink, structureSolid, BooleanOperationsType.Intersect)
                    ?.Volume ?? 0;
            } catch(Autodesk.Revit.Exceptions.ApplicationException) {
                continue;
            }

            // Больше половины объёма задания - дальше искать нечего, это и есть основа.
            if(volume >= half) {
                task.Host = element;
                return;
            }
            if(volume > best) {
                best = volume;
                host = element;
            }
        }

        task.Host = host;
    }
}
