using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Autodesk.Revit.DB;

namespace Juru.RevitPlugins.Openings.Models.Intersections;

/// <summary>
/// Ищет пересечения инженерных элементов с конструкциями из связей по геометрии солидов.
/// </summary>
/// <remarks>
/// Алгоритм повторяет проверку на коллизии из ClashNavigator: замкнутость солида определяется
/// до трансформации, потому что у трансформированного солида появляются лишние грани и рёбра;
/// сначала отсекаем по габаритному боксу, и только затем считаем булево пересечение.
/// В отличие от луча, это находит проходы соединительной арматуры и наклонные проходы,
/// а объём пересечения позволяет отличить настоящий проход от касания углом.
/// </remarks>
public class SolidIntersectionFinder {
    private readonly MepElementsProvider _mepProvider;

    public SolidIntersectionFinder(MepElementsProvider mepProvider) {
        _mepProvider = mepProvider ?? throw new ArgumentNullException(nameof(mepProvider));
    }

    /// <summary>
    /// Возвращает пересечения инженерных элементов с конструкциями из указанных связей.
    /// </summary>
    /// <param name="structureProviders">Связи с конструкциями.</param>
    /// <param name="progress">Прогресс по обработанным конструкциям.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public IReadOnlyCollection<MepStructureIntersection> Find(
        IReadOnlyCollection<StructureElementsProvider> structureProviders,
        IProgress<int> progress = null,
        CancellationToken cancellationToken = default) {

        if(structureProviders is null) { throw new ArgumentNullException(nameof(structureProviders)); }

        var candidates = _mepProvider.GetCandidates().ToDictionary(candidate => candidate.Element.Id);
        if(candidates.Count == 0) {
            return [];
        }

        var mepIds = candidates.Keys.ToList();
        List<MepStructureIntersection> intersections = [];
        int processed = 0;

        foreach(var structureProvider in structureProviders) {
            var transitionMatrix = _mepProvider.MainTransform.Inverse.Multiply(structureProvider.MainTransform);

            foreach(var structureElement in structureProvider.GetElements()) {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(++processed);

                foreach(var structureSolid in GetTransformedSolids(structureElement, transitionMatrix)) {
                    intersections.AddRange(FindForSolid(candidates, mepIds, structureProvider, structureElement, structureSolid));
                }
            }
        }

        return intersections;
    }

    private IEnumerable<Solid> GetTransformedSolids(Element element, Transform transitionMatrix) {
        foreach(var solid in GeometryUtils.GetSolids(element)) {
            Solid transformed;
            try {
                transformed = SolidUtils.CreateTransformed(solid, transitionMatrix);
            } catch(Autodesk.Revit.Exceptions.ApplicationException) {
                continue;
            }
            yield return transformed;
        }
    }

    private IEnumerable<MepStructureIntersection> FindForSolid(
        IReadOnlyDictionary<ElementId, MepElementCandidate> candidates,
        IList<ElementId> mepIds,
        StructureElementsProvider structureProvider,
        Element structureElement,
        Solid structureSolid) {

        var nearby = new FilteredElementCollector(_mepProvider.Doc, mepIds)
            .WherePasses(new BoundingBoxIntersectsFilter(GeometryUtils.GetOutline(structureSolid)))
            .ToElementIds();

        foreach(var mepId in nearby) {
            if(!candidates.TryGetValue(mepId, out var candidate)) {
                continue;
            }

            var intersection = GetIntersection(candidate.Element, structureSolid, out bool geometryFailed);
            if(intersection is null && !geometryFailed) {
                continue;
            }

            yield return new MepStructureIntersection(candidate, structureProvider, structureElement, intersection);
        }
    }

    /// <summary>
    /// Возвращает наибольший солид пересечения элемента с конструкцией,
    /// либо <c>null</c>, если пересечения нет или его геометрию вычислить не удалось.
    /// </summary>
    /// <param name="geometryFailed">
    /// True, если пересечение есть, но булева операция над геометрией не удалась.
    /// </param>
    private Solid GetIntersection(Element mepElement, Solid structureSolid, out bool geometryFailed) {
        geometryFailed = false;
        Solid biggest = null;

        foreach(var mepSolid in GeometryUtils.GetSolids(mepElement)) {
            try {
                var intersection = BooleanOperationsUtils.ExecuteBooleanOperation(
                    mepSolid,
                    structureSolid,
                    BooleanOperationsType.Intersect);
                if(intersection.Volume > 0 && intersection.Volume > (biggest?.Volume ?? 0)) {
                    biggest = intersection;
                }
            } catch(Autodesk.Revit.Exceptions.InvalidOperationException) {
                // Булева операция не всегда справляется с геометрией элемента. Пересечение при этом
                // может существовать - проверяем его фильтром, и отдаём наверх без габаритов,
                // чтобы такое пересечение попало в отчёт, а не потерялось молча.
                if(new ElementIntersectsSolidFilter(structureSolid).PassesFilter(mepElement)) {
                    geometryFailed = true;
                }
            }
        }

        if(biggest is not null) {
            geometryFailed = false;
        }
        return biggest;
    }
}
