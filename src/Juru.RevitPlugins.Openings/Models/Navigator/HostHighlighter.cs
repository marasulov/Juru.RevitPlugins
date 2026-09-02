using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

namespace Juru.RevitPlugins.Openings.Models.Navigator;

/// <summary>
/// Хост задания на отверстие - стена или перекрытие - вместе со связью, из которой он взят.
/// <c>Link</c> - <c>null</c> для хоста, лежащего прямо в активном документе (входящий навигатор
/// в файле АР/КР: там хост локальный, связь на инженера не заводится).
/// </summary>
public readonly record struct HostReference(ElementId Id, RevitLinkInstance? Link);

/// <summary>
/// Отпечаток хоста из связи - имя типа плюс характерный размер - раз с элементом в связи нельзя
/// сравнить напрямую по <see cref="ElementId"/> средствами <see cref="ParameterFilterElement"/>.
/// </summary>
internal readonly record struct HostFingerprint(string TypeName, BuiltInParameter SizeParam, double Size);

/// <summary>
/// Подсвечивает хост задания на клик "Показать": все остальные стены и перекрытия притушены,
/// выбранный хост виден чётко.
/// </summary>
/// <remarks>
/// Локальный хост (входящий навигатор) подсвечивается прямым переопределением графики элемента -
/// оно всегда перекрывает переопределение по фильтру, поэтому достаточно один раз притушить всю
/// категорию фильтром (<see cref="NavigatorColorScheme"/>) и точечно "включать" выбранный хост.
/// Для хоста из связи (исходящий навигатор) прямое переопределение элемента API не поддерживает -
/// связь не даёт точечно переопределить графику одного элемента внутри себя. Вместо этого, как
/// у Bim4Everyone, используется фильтр "все стены/перекрытия, кроме этой" по отпечатку геометрии
/// (имя типа + длина/периметр) - хост лежит в связи, и обычным <c>ElementId</c> с ним не сравнить.
/// </remarks>
public static class HostHighlighter {
    private const string _wallFilterPrefix = "BIM_Openings_Хост_Стены_";
    private const string _floorFilterPrefix = "BIM_Openings_Хост_Перекрытия_";

    /// <summary>
    /// Снимает подсветку с прежнего хоста и подсвечивает новый. Любой из аргументов может быть
    /// <c>null</c> - при первом клике прежнего хоста нет, у задания без найденного хоста нового нет.
    /// </summary>
    public static void Apply(Document doc, View3D view, HostReference? previous, HostReference? next) {
        using var transaction = new Transaction(doc, "Подсветка хоста задания");
        transaction.Start();

        if(previous is { Link: null } prevLocal) {
            view.SetElementOverrides(prevLocal.Id, new OverrideGraphicSettings());
        }
        if(next is { Link: null } nextLocal) {
            view.SetElementOverrides(nextLocal.Id, GetHighlightSettings());
        }

        var linkedElement = next is { Link: not null } linked ? GetElement(linked) : null;
        string username = doc.Application.Username;
        ApplyLinkFilter(doc, view, _wallFilterPrefix + username, BuiltInCategory.OST_Walls,
            linkedElement is Wall wall ? GetWallFingerprint(wall) : null);
        ApplyLinkFilter(doc, view, _floorFilterPrefix + username, BuiltInCategory.OST_Floors,
            linkedElement is Floor floor ? GetFloorFingerprint(floor) : null);

        transaction.Commit();
    }

    private static Element? GetElement(HostReference host) {
        return host.Link?.GetLinkDocument()?.GetElement(host.Id);
    }

    private static OverrideGraphicSettings GetHighlightSettings() {
        var color = new Color(255, 140, 0);
        return new OverrideGraphicSettings()
            .SetSurfaceForegroundPatternColor(color)
            .SetCutForegroundPatternColor(color)
            .SetSurfaceTransparency(0)
            .SetHalftone(false);
    }

    private static OverrideGraphicSettings GetDimSettings() {
        return new OverrideGraphicSettings().SetHalftone(true).SetSurfaceTransparency(70);
    }

    /// <summary>
    /// Настраивает фильтр "все элементы категории, кроме подходящих под отпечаток" - подсвеченный
    /// хост из отпечатка не притушивается, остальные притушены. Отпечаток <c>null</c> означает,
    /// что сейчас ничего не подсвечено: фильтр снимается с вида, чтобы не притушивать всё подряд.
    /// </summary>
    private static void ApplyLinkFilter(
        Document doc,
        View3D view,
        string name,
        BuiltInCategory category,
        HostFingerprint? fingerprint) {

        var filter = new FilteredElementCollector(doc)
            .OfClass(typeof(ParameterFilterElement))
            .Cast<ParameterFilterElement>()
            .FirstOrDefault(item => item.Name == name);

        if(fingerprint is null) {
            if(filter is not null && view.GetFilters().Contains(filter.Id)) {
                view.RemoveFilter(filter.Id);
            }
            return;
        }

        filter ??= ParameterFilterElement.Create(doc, name, new List<ElementId> { new(category) });
        filter.SetCategories(new List<ElementId> { new(category) });
        filter.ClearRules();

        var typeNameId = new ElementId(BuiltInParameter.ALL_MODEL_TYPE_NAME);
        var sizeId = new ElementId(fingerprint.Value.SizeParam);

        // Двухаргументная перегрузка: правило и так сравнивает без учёта регистра, а та, что
        // принимала это третьим аргументом, в Revit 2026 убрана.
        var typeNameNotEquals = new ElementParameterFilter(
            ParameterFilterRuleFactory.CreateNotEqualsRule(typeNameId, fingerprint.Value.TypeName));
        // Эпсилон вместо точного равенства: то же измерение геометрии, которое дало отпечаток,
        // может при следующем запросе на сотую долю миллиметра отличаться от самого себя.
        double epsilon = UnitUtils.ConvertToInternalUnits(0.1, UnitTypeId.Millimeters);
        var sizeLess = new ElementParameterFilter(
            ParameterFilterRuleFactory.CreateLessRule(sizeId, fingerprint.Value.Size - epsilon, epsilon));
        var sizeGreater = new ElementParameterFilter(
            ParameterFilterRuleFactory.CreateGreaterRule(sizeId, fingerprint.Value.Size + epsilon, epsilon));

        // Совпадает с хостом только элемент того же типа и того же характерного размера - иначе
        // попадает под "притушить". Отдельного ElementId у элемента в связи для сравнения нет.
        filter.SetElementFilter(new LogicalOrFilter([typeNameNotEquals, sizeLess, sizeGreater]));

        if(!view.GetFilters().Contains(filter.Id)) {
            view.AddFilter(filter.Id);
        }
        view.SetFilterOverrides(filter.Id, GetDimSettings());
        view.SetFilterVisibility(filter.Id, true);
    }

    private static HostFingerprint GetWallFingerprint(Wall wall) {
        double length = wall.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH)?.AsDouble() ?? 0;
        return new HostFingerprint(wall.WallType.Name, BuiltInParameter.CURVE_ELEM_LENGTH, length);
    }

    private static HostFingerprint GetFloorFingerprint(Floor floor) {
        double perimeter = floor.get_Parameter(BuiltInParameter.HOST_PERIMETER_COMPUTED)?.AsDouble() ?? 0;
        return new HostFingerprint(floor.FloorType.Name, BuiltInParameter.HOST_PERIMETER_COMPUTED, perimeter);
    }
}
