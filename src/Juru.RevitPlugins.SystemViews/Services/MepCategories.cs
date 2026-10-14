using System.Collections.Generic;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.SystemViews.Models;

namespace Juru.RevitPlugins.SystemViews.Services;

/// <summary>
///     Категории, из которых строятся схемы, и какие из них выбраны по умолчанию.
/// </summary>
public static class MepCategories
{
    /// <summary>Все категории, которые предлагает окно, с разделом.</summary>
    public static IReadOnlyList<(BuiltInCategory Category, MepDiscipline Discipline)> All { get; } =
    [
        (BuiltInCategory.OST_DuctCurves, MepDiscipline.Hvac),
        (BuiltInCategory.OST_FlexDuctCurves, MepDiscipline.Hvac),
        (BuiltInCategory.OST_DuctFitting, MepDiscipline.Hvac),
        (BuiltInCategory.OST_DuctAccessory, MepDiscipline.Hvac),
        (BuiltInCategory.OST_DuctTerminal, MepDiscipline.Hvac),
        (BuiltInCategory.OST_DuctInsulations, MepDiscipline.Hvac),
        (BuiltInCategory.OST_DuctLinings, MepDiscipline.Hvac),
        (BuiltInCategory.OST_MechanicalEquipment, MepDiscipline.Hvac),

        (BuiltInCategory.OST_PipeCurves, MepDiscipline.Plumbing),
        (BuiltInCategory.OST_FlexPipeCurves, MepDiscipline.Plumbing),
        (BuiltInCategory.OST_PipeFitting, MepDiscipline.Plumbing),
        (BuiltInCategory.OST_PipeAccessory, MepDiscipline.Plumbing),
        (BuiltInCategory.OST_PipeInsulations, MepDiscipline.Plumbing),
        (BuiltInCategory.OST_PlumbingFixtures, MepDiscipline.Plumbing),
#if REVIT2024_OR_GREATER
        (BuiltInCategory.OST_PlumbingEquipment, MepDiscipline.Plumbing),
#endif
        (BuiltInCategory.OST_Sprinklers, MepDiscipline.Plumbing),

        (BuiltInCategory.OST_CableTray, MepDiscipline.Electrical),
        (BuiltInCategory.OST_CableTrayFitting, MepDiscipline.Electrical),
        (BuiltInCategory.OST_Conduit, MepDiscipline.Electrical),
        (BuiltInCategory.OST_ConduitFitting, MepDiscipline.Electrical)
    ];

    /// <summary>По умолчанию - ОВ и ВК: схемы по системам строят они, у ЭОМ свои схемы.</summary>
    public static bool IsDefault(MepDiscipline discipline)
    {
        return discipline is MepDiscipline.Hvac or MepDiscipline.Plumbing;
    }
}
