using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Openings.Models.Configs;
using Juru.RevitPlugins.Openings.Models.Intersections;

namespace Juru.RevitPlugins.Openings.Models.Navigator;

/// <summary>
/// Инженерные элементы активного файла для проверки состояния заданий.
/// </summary>
/// <remarks>
/// В отличие от выдачи заданий, отбор не сужается ни порогами габаритов, ни галочкой вида:
/// задание, выданное когда-то на трубу, которую потом уменьшили или чей вид сняли с расстановки,
/// осталось в модели, и в навигаторе оно должно опознаваться как задание с проходящей трубой,
/// а не как висящее в воздухе.
/// </remarks>
public class MepElementsIndex {
    private readonly Dictionary<ElementId, MepElementCandidate> _candidates = [];

    public MepElementsIndex(Document doc, OpeningsConfig config) {
        if(doc is null) { throw new ArgumentNullException(nameof(doc)); }
        if(config is null) { throw new ArgumentNullException(nameof(config)); }

        var categories = GetCategories(config);
        if(categories.Count == 0) {
            return;
        }

        var elements = new FilteredElementCollector(doc)
            .WherePasses(new ElementMulticategoryFilter(categories))
            .WhereElementIsNotElementType()
            .ToElements();

        foreach(var element in elements) {
            var size = MepElementSize.GetSize(element);
            if(size is null) {
                continue;
            }

            var kind = ResolveKind(element, size);
            // Настройки вида могут отсутствовать в чужом файле настроек, и элемент всё равно нужен:
            // без него задание, через которое он проходит, выглядело бы висящим в воздухе.
            // Отсутствие настроек проверяется там, где сравниваются зазоры.
            var settings = kind is null ? null : config.GetCategory(kind.Value);
            _candidates[element.Id] = new MepElementCandidate(element, settings!, size, IsFitting(element));
        }
    }

    /// <summary>
    /// Id всех инженерных элементов, участвующих в проверке.
    /// </summary>
    public ICollection<ElementId> Ids => _candidates.Keys;

    /// <summary>
    /// Возвращает элемент с настройками его вида, либо <c>null</c>, если элемент не отобран.
    /// </summary>
    public MepElementCandidate? Get(ElementId id) {
        return _candidates.TryGetValue(id, out var candidate) ? candidate : null;
    }

    /// <summary>
    /// Возвращает вид инженерного элемента по его категории и форме сечения.
    /// </summary>
    /// <remarks>
    /// Круглые и прямоугольные воздуховоды - одна категория Revit, но разные настройки зазоров,
    /// поэтому вид определяется вместе с формой сечения.
    /// </remarks>
    private static MepKind? ResolveKind(Element element, MepElementSize size) {
        switch(element.Category?.BuiltInCategory ?? BuiltInCategory.INVALID) {
            case BuiltInCategory.OST_PipeCurves:
            case BuiltInCategory.OST_PipeFitting:
            case BuiltInCategory.OST_PipeAccessory:
                return MepKind.Pipe;
            case BuiltInCategory.OST_DuctCurves:
            case BuiltInCategory.OST_DuctFitting:
            case BuiltInCategory.OST_DuctAccessory:
                return size.IsRound ? MepKind.RoundDuct : MepKind.RectangleDuct;
            case BuiltInCategory.OST_CableTray:
            case BuiltInCategory.OST_CableTrayFitting:
                return MepKind.CableTray;
            case BuiltInCategory.OST_Conduit:
            case BuiltInCategory.OST_ConduitFitting:
                return MepKind.Conduit;
            default:
                return null;
        }
    }

    private static bool IsFitting(Element element) {
        return element is FamilyInstance;
    }

    private static List<BuiltInCategory> GetCategories(OpeningsConfig config) {
        return config.Categories
            .SelectMany(settings => settings.Kind.GetCurveCategories()
                .Concat(settings.Kind.GetFittingCategories()))
            .Distinct()
            .ToList();
    }
}
