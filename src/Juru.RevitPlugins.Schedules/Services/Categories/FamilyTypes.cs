using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Schedules.Models;

using Juru.RevitPlugins.Schedules.Localization;

namespace Juru.RevitPlugins.Schedules.Services.Categories;

/// <summary>
///     Названия типоразмеров семейств.
/// </summary>
/// <remarks>
///     Только те, что действительно расставлены в модели: неиспользованных типоразмеров в файле бывают
///     тысячи, они не попадают ни на лист, ни в спецификацию, и переводить их — тратить перевод впустую.
///     Имя семейства при этом не переводится: оно меняется только в редакторе семейств, а не в проекте.
/// </remarks>
public sealed class FamilyTypes : TranslatableCategory
{
    public override string Key
    {
        get { return "FamilyTypes"; }
    }

    public override string Name
    {
        get { return Text.CategoryFamilyTypes; }
    }

    public override string EmptyNote
    {
        get { return Text.EmptyFamilyTypes; }
    }

    public override IReadOnlyList<ElementText> Collect(CollectScope scope)
    {
        var used = scope.AllInstances()
            .Select(element => element.GetTypeId())
            .Where(id => id != ElementId.InvalidElementId)
            .ToHashSet();

        var types = scope.WholeModel()
            .WhereElementIsElementType()
            .Where(type => used.Contains(type.Id))
            .Cast<ElementType>();

        return Order(types.SelectMany(type => DescribeAll(
            type,
            Describe(type),
            TextTarget.Name(Text.TargetTypeName))));
    }

    /// <summary>Семейство и категория: по ним типоразмер находится в диспетчере проекта.</summary>
    private static string Describe(ElementType type)
    {
        var family = type.FamilyName;
        var category = type.Category?.Name ?? string.Empty;

        if (family.Length is 0)
        {
            return category;
        }

        return category.Length is 0 ? family : $"{category} / {family}";
    }
}
