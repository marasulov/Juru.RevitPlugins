using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Schedules.Models;

namespace Juru.RevitPlugins.Schedules.Services;

/// <summary>
///     Отбирает параметры элементов, которые можно безопасно перевести значением. Правила повторяют
///     защиту из AUTOM8LABS.Transl8: параметры, по которым спецификации сортируют, группируют или
///     находят элемент — Mark, Type Name, Family Name, Room Name и подобные, — не трогаются, даже если
///     это обычный общий или проектный параметр. Из встроенных параметров переводится только заведомо
///     неключевой текст: Ревит использует большинство <see cref="BuiltInParameter" /> для внутренних
///     связей, и переписывать их значение рискованно.
/// </summary>
public static class ElementParameterSafety
{
    /// <summary>
    ///     Категории, где значения параметров стоит проверить. Список — рабочие категории модели, а не
    ///     аннотации и не системные семейства, у которых текст переводится своей отдельной вкладкой
    ///     (помещения, зоны, виды, листы, материалы, размеры и т.д.).
    /// </summary>
    public static readonly BuiltInCategory[] Categories =
    [
        BuiltInCategory.OST_Doors,
        BuiltInCategory.OST_Windows,
        BuiltInCategory.OST_Walls,
        BuiltInCategory.OST_Floors,
        BuiltInCategory.OST_Ceilings,
        BuiltInCategory.OST_Roofs,
        BuiltInCategory.OST_Stairs,
        BuiltInCategory.OST_Ramps,
        BuiltInCategory.OST_Columns,
        BuiltInCategory.OST_StructuralColumns,
        BuiltInCategory.OST_StructuralFraming,
        BuiltInCategory.OST_StructuralFoundation,
        BuiltInCategory.OST_Furniture,
        BuiltInCategory.OST_FurnitureSystems,
        BuiltInCategory.OST_Casework,
        BuiltInCategory.OST_GenericModel,
        BuiltInCategory.OST_Planting,
        BuiltInCategory.OST_Site,
        BuiltInCategory.OST_Parking,
        BuiltInCategory.OST_MechanicalEquipment,
        BuiltInCategory.OST_ElectricalEquipment,
        BuiltInCategory.OST_ElectricalFixtures,
        BuiltInCategory.OST_LightingFixtures,
        BuiltInCategory.OST_PlumbingFixtures,
        BuiltInCategory.OST_Sprinklers,
        BuiltInCategory.OST_PipeCurves,
        BuiltInCategory.OST_DuctCurves,
        BuiltInCategory.OST_CableTray,
        BuiltInCategory.OST_Conduit,
        BuiltInCategory.OST_PipeAccessory,
        BuiltInCategory.OST_PipeFitting,
        BuiltInCategory.OST_DuctAccessory,
        BuiltInCategory.OST_DuctFitting,
        BuiltInCategory.OST_CurtainWallPanels,
        BuiltInCategory.OST_CurtainWallMullions,
        BuiltInCategory.OST_Railings,
        BuiltInCategory.OST_SpecialityEquipment,
        BuiltInCategory.OST_Entourage
    ];

    /// <summary>
    ///     Имена параметров, которые уже переводятся своей целью на другой вкладке или служат ключом
    ///     сортировки/группировки/поиска элемента в спецификации. Их значение остаётся как есть.
    /// </summary>
    private static readonly HashSet<string> ProtectedParameterNames = new(StringComparer.CurrentCultureIgnoreCase)
    {
        "Name",
        "Sheet Name",
        "Sheet Number",
        "Title on Sheet",
        "Comments",
        "Department",
        "Description",
        "Mark",
        "Type Name",
        "Family Name",
        "Family and Type",
        "Room Name",
        "Room Number",
        "Room Department",
        "Room Comments",
        "Occupancy",
        "Base Finish",
        "Ceiling Finish",
        "Wall Finish",
        "Floor Finish",
        "View Name",
        "Keynote",
        "Keywords"
    };

    /// <summary>
    ///     Единственные встроенные параметры, чьё значение разрешено переводить. Всё остальное в
    ///     <see cref="BuiltInParameter" /> Ревит может использовать для внутреннего сопоставления, поэтому
    ///     по умолчанию встроенные параметры не трогаются.
    /// </summary>
    private static readonly HashSet<BuiltInParameter> TranslatableBuiltInParameters =
    [
        BuiltInParameter.ALL_MODEL_TYPE_COMMENTS,
        BuiltInParameter.ALL_MODEL_DESCRIPTION
    ];

    /// <summary>
    ///     Параметры одного элемента, значения которых можно перевести: строковые, доступные для записи,
    ///     не входящие в список защищённых имён — и для встроенных дополнительно из разрешённого набора.
    /// </summary>
    public static IEnumerable<TextTarget> ResolveTargets(Element element)
    {
        foreach (Parameter parameter in element.Parameters)
        {
            if (parameter.StorageType is not StorageType.String || parameter.IsReadOnly)
            {
                continue;
            }

            var definition = parameter.Definition;
            var name = definition.Name;

            if (definition is InternalDefinition { BuiltInParameter: var builtInParameter } &&
                builtInParameter != BuiltInParameter.INVALID)
            {
                if (TranslatableBuiltInParameters.Contains(builtInParameter))
                {
                    yield return TextTarget.Parameter(builtInParameter, name);
                }

                continue;
            }

            if (!ProtectedParameterNames.Contains(name))
            {
                yield return TextTarget.Parameter(name);
            }
        }
    }
}
