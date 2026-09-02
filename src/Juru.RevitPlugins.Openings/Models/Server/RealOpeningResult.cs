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
    /// Согласовано не обеими дисциплинами - прорезать такое задание нельзя.
    /// </summary>
    public int SkippedNotApproved { get; set; }

    /// <summary>
    /// Экземпляра-задания в модели не нашлось, а вырезающее отверстие строится по его точке
    /// и уровню - чаще всего это значит, что само задание поставить не удалось.
    /// </summary>
    public int SkippedNoTaskInstance { get; set; }

    /// <summary>
    /// Под точкой задания нет стены или перекрытия, в которое можно вставить хостовое
    /// вырезающее семейство.
    /// </summary>
    public int SkippedNoHost { get; set; }

    /// <summary>
    /// Действующие сейчас чистовые отверстия по ключу задания - и уже стоявшие, и только что
    /// прорезанные. Навигатору нужно показывать/выделять именно их для согласованных заданий,
    /// а не экземпляр-задание, который на этом же прогоне уже удалён.
    /// </summary>
    public Dictionary<string, ElementId> RealElements { get; } = [];
}
