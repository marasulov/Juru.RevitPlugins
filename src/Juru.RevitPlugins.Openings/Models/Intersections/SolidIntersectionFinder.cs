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
/// Порядок такой: отсечь по габаритному боксу, подтвердить пересечение штатным
/// <see cref="ElementIntersectsSolidFilter"/> и только затем считать булево пересечение — оно нужно
/// для габаритов отверстия, а не для ответа на вопрос, есть ли пересечение вообще. Замер на живой
/// модели: фильтр Revit нашёл 8 пар, булева операция — 4 из них, остальные она возвращала с нулевым
/// объёмом без исключения. Если считать пересечением только удавшееся булево, половина проходов
/// исчезает молча.
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
            .ToElementIds()
            .ToList();
        if(nearby.Count == 0) {
            yield break;
        }

        // Пересекается ли элемент с конструкцией, решает Revit: его фильтр справляется с геометрией,
        // на которой булева операция возвращает пустой результат.
        var intersecting = new FilteredElementCollector(_mepProvider.Doc, nearby)
            .WherePasses(new ElementIntersectsSolidFilter(structureSolid))
            .ToElementIds();

        foreach(var mepId in intersecting) {
            if(!candidates.TryGetValue(mepId, out var candidate)) {
                continue;
            }

            // Габариты берём из булева пересечения, но его неудача пересечение не отменяет:
            // такое попадёт в отчёт как невычислимая геометрия, а не потеряется.
            var intersection = GetIntersection(candidate.Element, structureSolid);
            yield return new MepStructureIntersection(candidate, structureProvider, structureElement, intersection);
        }
    }

    /// <summary>
    /// Возвращает наибольший солид пересечения элемента с конструкцией, либо <c>null</c>,
    /// если булева операция ничего не дала: сам факт пересечения к этому моменту уже подтверждён.
    /// </summary>
    private Solid GetIntersection(Element mepElement, Solid structureSolid) {
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
                // Геометрия элемента не поддалась булевой операции: габариты по ней не построить.
            }
        }

        return biggest;
    }
}
