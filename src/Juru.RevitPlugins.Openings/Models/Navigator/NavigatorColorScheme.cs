using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

namespace Juru.RevitPlugins.Openings.Models.Navigator;

/// <summary>
/// Постоянная раскраска вида навигатора: зелёные инженерные системы, красные задания,
/// серые конструкции, притушенное всё остальное - как у Bim4Everyone.
/// </summary>
/// <remarks>
/// Раскраска не зависит от кликов и настраивается один раз на вид (фильтры находятся по имени
/// и переиспользуются, как и сам вид - <see cref="NavigatorViewProvider"/>). Для исходящего
/// навигатора (файл инженера) есть все три категории, для входящего (файл АР/КР) - только
/// задания и конструкции: реального инженерного элемента в этом файле нет, связь на него
/// принципиально не заводится (<c>docs/OpeningsApi.md</c>).
/// </remarks>
public static class NavigatorColorScheme {
    /// <summary>
    /// Раскрашивает вид для исходящего навигатора: зелёные инженерные системы, красные задания,
    /// серые конструкции из связей, притушенное остальное.
    /// </summary>
    public static void ApplyOutgoing(
        Document doc,
        View3D view,
        IReadOnlyCollection<BuiltInCategory> mepCategories,
        IReadOnlyCollection<BuiltInCategory> structureCategories) {

        using var transaction = new Transaction(doc, "Раскраска вида навигатора");
        transaction.Start();

        if(mepCategories.Count > 0) {
            ApplyFilter(doc, view, "BIM_Openings_МЕП", mepCategories, GetColorSettings(doc, new Color(0, 128, 0), 10));
        }
        ApplyFilter(doc, view, "BIM_Openings_Задания", GetTaskCategories(), GetColorSettings(doc, new Color(255, 0, 0), 0));
        if(structureCategories.Count > 0) {
            ApplyFilter(doc, view, "BIM_Openings_Конструкции", structureCategories, GetColorSettings(doc, new Color(128, 128, 128), 0));
        }

        var secondary = GetSecondaryCategories(doc, mepCategories, structureCategories);
        if(secondary.Count > 0) {
            ApplyFilter(doc, view, "BIM_Openings_Прочее", secondary, GetDimSettings());
        }

        transaction.Commit();
    }

    /// <summary>
    /// Раскрашивает вид для входящего навигатора: красные задания, притушенные конструкции.
    /// Реальных инженерных элементов в этом файле нет - связь на них не заводится.
    /// </summary>
    public static void ApplyIncoming(Document doc, View3D view, IReadOnlyCollection<BuiltInCategory> structureCategories) {
        using var transaction = new Transaction(doc, "Раскраска вида навигатора");
        transaction.Start();

        ApplyFilter(doc, view, "BIM_Openings_Задания", GetTaskCategories(), GetColorSettings(doc, new Color(255, 0, 0), 0));
        if(structureCategories.Count > 0) {
            ApplyFilter(doc, view, "BIM_Openings_Конструкции", structureCategories, GetDimSettings());
        }

        transaction.Commit();
    }

    private static IReadOnlyCollection<BuiltInCategory> GetTaskCategories() {
        // Все семейства заданий - обобщённые модели (docs/OpeningTaskFamilies.md), задание
        // не различить от прочих обобщённых моделей категорией, но других занятых под задания
        // категорий в проекте обычно нет - подсветка всей категории целиком приемлема.
        return [BuiltInCategory.OST_GenericModel];
    }

    private static IReadOnlyCollection<BuiltInCategory> GetSecondaryCategories(
        Document doc,
        IReadOnlyCollection<BuiltInCategory> mepCategories,
        IReadOnlyCollection<BuiltInCategory> structureCategories) {

        var main = new HashSet<BuiltInCategory>(mepCategories);
        main.UnionWith(structureCategories);
        main.UnionWith(GetTaskCategories());
        main.Add(BuiltInCategory.OST_RvtLinks);

        var all = new HashSet<BuiltInCategory>();
        foreach(Category category in doc.Settings.Categories) {
            if(category.CategoryType == CategoryType.Model) {
                all.Add(category.BuiltInCategory);
            }
        }

        all.ExceptWith(main);
        return all;
    }

    private static void ApplyFilter(
        Document doc,
        View3D view,
        string name,
        IReadOnlyCollection<BuiltInCategory> categories,
        OverrideGraphicSettings settings) {

        var categoryIds = GetFilterableIds(categories);
        if(categoryIds.Count == 0) {
            return;
        }

        var filter = new FilteredElementCollector(doc)
            .OfClass(typeof(ParameterFilterElement))
            .Cast<ParameterFilterElement>()
            .FirstOrDefault(item => item.Name == name)
            ?? ParameterFilterElement.Create(doc, name, categoryIds);

        filter.SetCategories(categoryIds);

        if(!view.GetFilters().Contains(filter.Id)) {
            view.AddFilter(filter.Id);
        }
        view.SetFilterOverrides(filter.Id, settings);
        view.SetFilterVisibility(filter.Id, true);
    }

    /// <summary>
    /// Оставляет от набора только те категории, по которым Revit разрешает делать фильтр вида.
    /// </summary>
    /// <remarks>
    /// Нефильтруемая категория в наборе валит создание <see cref="ParameterFilterElement"/>
    /// внутренней ошибкой Revit, а не понятным исключением. Набор модельных категорий зависит
    /// от версии Revit и от того, что загружено в файл, поэтому проверять его надо в рантайме,
    /// а не составлять список руками: для фильтра "Прочее" в него попадают все категории модели.
    /// </remarks>
    private static IList<ElementId> GetFilterableIds(IReadOnlyCollection<BuiltInCategory> categories) {
        var filterable = ParameterFilterUtilities.GetAllFilterableCategories();
        return categories
            .Select(category => new ElementId(category))
            .Where(filterable.Contains)
            .ToList();
    }

    private static OverrideGraphicSettings GetColorSettings(Document doc, Color color, int surfaceTransparency) {
        var settings = new OverrideGraphicSettings()
            .SetCutBackgroundPatternColor(color)
            .SetCutForegroundPatternColor(color)
            .SetSurfaceBackgroundPatternColor(color)
            .SetSurfaceForegroundPatternColor(color);

        if(surfaceTransparency > 0) {
            settings.SetSurfaceTransparency(surfaceTransparency);
        }

        var solidFill = new FilteredElementCollector(doc)
            .OfClass(typeof(FillPatternElement))
            .Cast<FillPatternElement>()
            .FirstOrDefault(pattern => pattern.GetFillPattern().IsSolidFill);
        if(solidFill is not null) {
            settings
                .SetSurfaceBackgroundPatternId(solidFill.Id)
                .SetSurfaceForegroundPatternId(solidFill.Id);
        }

        return settings;
    }

    private static OverrideGraphicSettings GetDimSettings() {
        return new OverrideGraphicSettings().SetHalftone(true).SetSurfaceTransparency(70);
    }
}
