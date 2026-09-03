using System.Collections.Generic;
using System.Linq;

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

    /// <summary>
    /// Сколько уже стоявших заданий подвинуто на новое место - тот же инженерный элемент,
    /// но прошёл в другой точке (см. <see cref="PlacedTasksIndex.FindBySourceMep"/>), поэтому
    /// не дубль, а обновление на месте.
    /// </summary>
    public int Updated { get; set; }

    /// <summary>
    /// Параметры, которых не оказалось в семействах: значения для них посчитаны, но записать некуда.
    /// </summary>
    public SortedSet<string> MissingParameters { get; } = [];

    /// <summary>
    /// Семейства, обновлённые эталоном из папки эталонных семейств: в стоявших в проекте
    /// не хватало параметров - значит, они не последней версии.
    /// </summary>
    public SortedSet<string> UpdatedFamilies { get; } = [];

    /// <summary>
    /// Семейства, которых в проекте не было и которые пришлось загрузить из папки эталонных семейств.
    /// </summary>
    public SortedSet<string> LoadedFamilies { get; } = [];

    /// <summary>
    /// Причины отказов с количеством пересечений по каждой.
    /// </summary>
    public IReadOnlyCollection<KeyValuePair<string, int>> GetFailureSummary() {
        return Failed
            .GroupBy(failure => failure.Reason)
            .Select(group => new KeyValuePair<string, int>(group.Key, group.Count()))
            .OrderByDescending(pair => pair.Value)
            .ToArray();
    }
}
