using System.Collections.Generic;

using Autodesk.Revit.DB;

namespace Juru.RevitPlugins.Openings.Models.Configs;

/// <summary>
/// Сопоставление видов элементов и конструкций категориям Revit.
/// </summary>
public static class MepKindExtensions {
    /// <summary>
    /// Возвращает категории линейных элементов указанного вида.
    /// </summary>
    public static IReadOnlyCollection<BuiltInCategory> GetCurveCategories(this MepKind kind) {
        switch(kind) {
            case MepKind.Pipe:
                return [BuiltInCategory.OST_PipeCurves];
            case MepKind.RoundDuct:
            case MepKind.RectangleDuct:
                return [BuiltInCategory.OST_DuctCurves];
            case MepKind.CableTray:
                return [BuiltInCategory.OST_CableTray];
            case MepKind.Conduit:
                return [BuiltInCategory.OST_Conduit];
            default:
                return [];
        }
    }

    /// <summary>
    /// Возвращает категории соединительных элементов указанного вида.
    /// </summary>
    /// <remarks>
    /// Отводы и тройники - это <see cref="FamilyInstance"/> без осевой линии, поэтому они ищутся
    /// отдельно от линейных элементов, но задания на отверстия для них нужны так же.
    /// </remarks>
    public static IReadOnlyCollection<BuiltInCategory> GetFittingCategories(this MepKind kind) {
        switch(kind) {
            case MepKind.Pipe:
                return [BuiltInCategory.OST_PipeFitting, BuiltInCategory.OST_PipeAccessory];
            case MepKind.RoundDuct:
            case MepKind.RectangleDuct:
                return [BuiltInCategory.OST_DuctFitting, BuiltInCategory.OST_DuctAccessory];
            case MepKind.CableTray:
                return [BuiltInCategory.OST_CableTrayFitting];
            case MepKind.Conduit:
                return [BuiltInCategory.OST_ConduitFitting];
            default:
                return [];
        }
    }

    /// <summary>
    /// Проверяет, круглое ли сечение у элементов указанного вида.
    /// </summary>
    public static bool IsRound(this MepKind kind) {
        return kind is MepKind.Pipe or MepKind.RoundDuct or MepKind.Conduit;
    }

    /// <summary>
    /// Возвращает категорию Revit для вида конструкции.
    /// </summary>
    public static BuiltInCategory GetCategory(this StructureKind kind) {
        return kind == StructureKind.Wall ? BuiltInCategory.OST_Walls : BuiltInCategory.OST_Floors;
    }
}
