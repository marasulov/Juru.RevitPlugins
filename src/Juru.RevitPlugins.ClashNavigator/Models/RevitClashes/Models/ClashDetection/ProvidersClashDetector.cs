using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.ClashNavigator.Models.Clashes;
using Juru.RevitPlugins.ClashNavigator.Models.Extensions;
using Juru.RevitPlugins.ClashNavigator.Models.Interfaces;
using Juru.RevitPlugins.ClashNavigator.Services;

namespace Juru.RevitPlugins.ClashNavigator.Models.ClashDetection;
internal class ProvidersClashDetector {
    private readonly RevitRepository _revitRepository;
    private readonly IProvider _firstProvider;
    private readonly IProvider _secondProvider;
    private readonly List<ElementId> _ids;
    private readonly List<Element> _mainElements;
    private readonly List<Element> _secondElements;
    private readonly double _tolerance;
    private readonly ClashIgnoreRules _ignoreRules;


    public ProvidersClashDetector(
        RevitRepository revitRepository,
        IProvider firstProvider,
        IProvider secondProvider,
        double tolerance = 0,
        ClashIgnoreRules ignoreRules = ClashIgnoreRules.None) {

        _tolerance = Math.Max(0, tolerance);
        _ignoreRules = ignoreRules;

        _revitRepository = revitRepository ?? throw new ArgumentNullException(nameof(revitRepository));
        _firstProvider = firstProvider ?? throw new ArgumentNullException(nameof(firstProvider));
        _secondProvider = secondProvider ?? throw new ArgumentNullException(nameof(secondProvider));

        _mainElements = _firstProvider.GetElements();
        _ids = _mainElements.Select(item => item.Id).ToList();

        _secondElements = _secondProvider.GetElements();
    }

    /// <param name="progress">Окно хода поиска; этап задаётся здесь, по числу элементов второй выборки.</param>
    /// <param name="stage">Подпись этапа в окне хода поиска.</param>
    public List<ClashModel> GetClashes(ClashSearchProgress progress, string stage) {
        if(_mainElements.Count == 0) {
            return [];
        }
        progress.StartStage(stage, _secondElements.Count);
        return GetClashes(progress, progress.Token).Distinct().ToList();
    }

    private List<ClashModel> GetClashes(IProgress<int> progress, CancellationToken ct) {
        List<ClashModel> clashes = [];

        if(_mainElements.Count == 0) {
            return clashes;
        }

        var resultTransform = _firstProvider.MainTransform.GetTransitionMatrix(_secondProvider.MainTransform);

        int count = 0;
        foreach(var element in _secondElements) {
            ct.ThrowIfCancellationRequested();
            progress.Report(count);
            count++;

            var solids = _secondProvider.GetSolids(element)
                                        .ToArray();

            var notNullSolids = solids.Where(s => s != null);
            // Solid-ы необходимо проверять на замкнутость геометрии до трансформации, так как у трансформированного solid-а 
            // появляются грани и ребра
            var closedSolids = notNullSolids.Where(item => !item.IsNotClosed())
                                     .Select(item => SolidUtils.CreateTransformed(item, resultTransform));

            var openSolids = notNullSolids.Where(item => item.IsNotClosed())
                                   .Select(item => SolidUtils.CreateTransformed(item, resultTransform));

            foreach(var solid in openSolids) {
                clashes.AddRange(GetElementClashesWithFilter(element, solid));
            }

            foreach(var solid in closedSolids) {
                clashes.AddRange(GetElementClashesWithIntersection(element, solid));
            }
        }

        return clashes;
    }

    private IEnumerable<ClashModel> GetElementClashesWithFilter(Element element, Solid solid) {
        return new FilteredElementCollector(_firstProvider.Doc, _ids)
            .Excluding(new ElementId[] { element.Id })
            .WherePasses(new BoundingBoxIntersectsFilter(solid.GetOutline()))
            .WherePasses(new ElementIntersectsSolidFilter(solid))
            .Where(item => !ClashIgnoreRuleMatcher.ShouldIgnore(item, element, _ignoreRules))
            .Select(item => new ClashModel(
                _revitRepository,
                item,
                _firstProvider.MainTransform,
                element,
                _secondProvider.MainTransform));
    }

    private IEnumerable<ClashModel> GetElementClashesWithIntersection(Element element, Solid solid) {
        return new FilteredElementCollector(_firstProvider.Doc, _ids)
                .Excluding(new ElementId[] { element.Id })
                .WherePasses(new BoundingBoxIntersectsFilter(solid.GetOutline()))
                .Where(item => !ClashIgnoreRuleMatcher.ShouldIgnore(item, element, _ignoreRules))
                .Where(item => HasSolidsIntersection(item, solid))
                .Select(item => new ClashModel(
                    _revitRepository,
                    item,
                    _firstProvider.MainTransform,
                    element,
                    _secondProvider.MainTransform));
    }

    private bool HasSolidsIntersection(Element element, Solid solid) {
        foreach(var s in _firstProvider.GetSolids(element)) {
            if(s != null && s.IsNotClosed()) {
                return new ElementIntersectsSolidFilter(solid).PassesFilter(element);
            }
            if(HasSolidIntersection(element, s, solid)) {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Глубина пересечения - наименьший размер габарита тела пересечения.
    /// </summary>
    /// <remarks>
    /// Приближение глубины проникновения, которой допуск задаётся в Navisworks: для труб и
    /// воздуховодов, пересекающих конструкцию, это меньшее из толщины конструкции и сечения,
    /// и касания с нахлёстом в доли миллиметра им надёжно отсекаются. Габарит тела у Ревита
    /// выровнен по его собственной системе координат, а для косых пересечений он больше
    /// истинной глубины - такие пересечения допуском отсекаются реже, а не чаще.
    /// </remarks>
    private static double GetDepth(Solid intersection) {
        var box = intersection.GetBoundingBox();
        var size = box.Max - box.Min;
        return Math.Min(size.X, Math.Min(size.Y, size.Z));
    }

    private bool HasSolidIntersection(Element element, Solid solid0, Solid solid1) {
        try {
            var intersection = BooleanOperationsUtils.ExecuteBooleanOperation(
                solid0,
                solid1,
                BooleanOperationsType.Intersect);
            return intersection.Volume > 0 && GetDepth(intersection) >= _tolerance;
        } catch(Autodesk.Revit.Exceptions.InvalidOperationException) {
            // будем считать, что коллизия есть, но с кривой геометрией.
            // Далее её можно будет проверить по объему и проценту пересечения в ClashData
            return true;
        }
    }
}
