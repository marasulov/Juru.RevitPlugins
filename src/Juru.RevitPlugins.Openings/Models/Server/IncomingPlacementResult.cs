using System.Collections.Generic;

using Autodesk.Revit.DB;

namespace Juru.RevitPlugins.Openings.Models.Server;

/// <summary>
/// Результат постановки заданий, загруженных с сервера, в файле АР или КР.
/// </summary>
public class IncomingPlacementResult {
    /// <summary>
    /// Новых заданий поставлено.
    /// </summary>
    public int Added { get; set; }

    /// <summary>
    /// Существующих заданий обновлено (положение, габариты, поворот, параметры).
    /// </summary>
    public int Updated { get; set; }

    /// <summary>
    /// Заданий убрано из модели - инженер отметил их удалёнными.
    /// </summary>
    public int Removed { get; set; }

    /// <summary>
    /// Семейства, не загруженные в проект - задания этого типа не поставлены.
    /// </summary>
    public SortedSet<string> MissingFamilies { get; } = [];

    /// <summary>
    /// Параметры, которых не оказалось в семействах.
    /// </summary>
    public SortedSet<string> MissingParameters { get; } = [];

    /// <summary>
    /// Экземпляры, стоящие в модели после этого запуска, по ключу задания - и только что созданные,
    /// и уже стоявшие. Убранных (<see cref="Removed"/>) здесь нет. Нужен навигатору, чтобы выделять
    /// задание в модели по строке списка, не разбирая заново все экземпляры в файле.
    /// </summary>
    public Dictionary<string, ElementId> PlacedElements { get; } = [];
}
