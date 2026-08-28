using System.Collections.Generic;

using Autodesk.Revit.DB;

namespace Juru.RevitPlugins.Openings.Models.Server;

/// <summary>
/// Итог одного прогона <see cref="RealOpeningReplacer"/>.
/// </summary>
public class RealOpeningResult {
    /// <summary>
    /// Сколько заданий, согласованных своей дисциплиной, прорезаны вырезающим семейством.
    /// </summary>
    public int Placed { get; set; }

    /// <summary>
    /// Сколько уже вырезанных отверстий возвращены обратно в задание - решение сбросили
    /// или его отклонили.
    /// </summary>
    public int Reverted { get; set; }

    /// <summary>
    /// Вырезающие семейства, которых не нашлось в проекте - для таких заданий прорезание
    /// пропущено, а не провалено молча.
    /// </summary>
    public List<string> MissingFamilies { get; } = [];

    /// <summary>
    /// Действующие сейчас чистовые отверстия по ключу задания - и уже стоявшие, и только что
    /// прорезанные. Навигатору нужно показывать/выделять именно их для согласованных заданий,
    /// а не экземпляр-задание, который на этом же прогоне уже удалён.
    /// </summary>
    public Dictionary<string, ElementId> RealElements { get; } = [];
}
