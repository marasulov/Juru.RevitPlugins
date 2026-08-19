using System.Collections.Generic;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Openings.Models.Intersections;

namespace Juru.RevitPlugins.Openings.Models.Placement;

/// <summary>
/// Результат расстановки заданий на отверстия.
/// </summary>
public class PlacementResult {
    /// <summary>
    /// Размещённые задания.
    /// </summary>
    public List<FamilyInstance> Placed { get; } = [];

    /// <summary>
    /// Пересечения, для которых задание разместить не удалось, с причинами.
    /// </summary>
    public List<IntersectionRejection> Failed { get; } = [];

    /// <summary>
    /// Сколько заданий пропущено, потому что такое же уже стоит в модели.
    /// </summary>
    public int SkippedAsDuplicate { get; set; }
}
