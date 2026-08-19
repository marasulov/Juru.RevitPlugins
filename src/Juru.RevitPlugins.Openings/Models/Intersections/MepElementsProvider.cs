using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Openings.Models.Configs;

namespace Juru.RevitPlugins.Openings.Models.Intersections;

/// <summary>
/// Отбирает инженерные элементы активного файла по настройкам выдачи заданий.
/// </summary>
public class MepElementsProvider : IElementsProvider {
    private readonly OpeningsConfig _config;
    private readonly IReadOnlyCollection<ElementId> _elementsToFilter;
    private List<MepElementCandidate> _candidates;

    /// <param name="doc">Активный документ инженерных систем.</param>
    /// <param name="config">Настройки выдачи заданий.</param>
    /// <param name="elementsToFilter">
    /// Элементы, которыми ограничен поиск. Пустой список означает поиск по всему документу.
    /// Нужно для выдачи заданий по выбранным элементам.
    /// </param>
    public MepElementsProvider(Document doc, OpeningsConfig config, IReadOnlyCollection<ElementId> elementsToFilter = null) {
        Doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _elementsToFilter = elementsToFilter ?? [];
    }

    public Document Doc { get; }

    public Transform MainTransform => Transform.Identity;

    public IReadOnlyCollection<Element> GetElements() {
        return GetCandidates().Select(candidate => candidate.Element).ToArray();
    }

    /// <summary>
    /// Возвращает отобранные элементы вместе с настройками их вида.
    /// Результат вычисляется один раз: обход геометрии документа дорог.
    /// </summary>
    public IReadOnlyCollection<MepElementCandidate> GetCandidates() {
        return _candidates ??= CollectCandidates().ToList();
    }

    private IEnumerable<MepElementCandidate> CollectCandidates() {
        foreach(var settings in _config.GetSelectedCategories()) {
            foreach(var candidate in CollectByKind(settings, isFitting: false)) {
                yield return candidate;
            }
            foreach(var candidate in CollectByKind(settings, isFitting: true)) {
                yield return candidate;
            }
        }
    }

    private IEnumerable<MepElementCandidate> CollectByKind(MepCategorySettings settings, bool isFitting) {
        var categories = isFitting
            ? settings.Kind.GetFittingCategories()
            : settings.Kind.GetCurveCategories();
        if(categories.Count == 0) {
            yield break;
        }

        var elements = GetCollector()
            .WherePasses(new ElementMulticategoryFilter(categories.ToList()))
            .WhereElementIsNotElementType()
            .ToElements();

        foreach(var element in elements) {
            var size = MepElementSize.GetSize(element);
            if(size is null) {
                continue;
            }

            // Круглые и прямоугольные воздуховоды - это одна категория Revit, но разные настройки,
            // поэтому вид отбирается по форме сечения, а не только по категории.
            if(size.IsRound != settings.Kind.IsRound()) {
                continue;
            }

            double sizeMm = UnitUtils.ConvertFromInternalUnits(size.GoverningSize, UnitTypeId.Millimeters);
            if(!settings.SizeInRange(sizeMm)) {
                continue;
            }

            yield return new MepElementCandidate(element, settings, size, isFitting);
        }
    }

    private FilteredElementCollector GetCollector() {
        return _elementsToFilter.Count > 0
            ? new FilteredElementCollector(Doc, _elementsToFilter.ToList())
            : new FilteredElementCollector(Doc);
    }
}
