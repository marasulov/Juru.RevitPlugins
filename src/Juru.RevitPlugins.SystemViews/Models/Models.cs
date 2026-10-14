using System.Collections.Generic;

using Autodesk.Revit.DB;

namespace Juru.RevitPlugins.SystemViews.Models;

/// <summary>Раздел, к которому относится категория: по нему категории группируются в окне.</summary>
public enum MepDiscipline
{
    Hvac,
    Plumbing,
    Electrical
}

/// <summary>Категория инженерных элементов, из которых строятся схемы.</summary>
/// <param name="Category">Категория Ревита.</param>
/// <param name="Name">Имя категории на языке Ревита.</param>
/// <param name="Discipline">Раздел.</param>
/// <param name="ElementCount">Сколько элементов категории видно на исходном виде.</param>
public sealed record MepCategoryInfo(BuiltInCategory Category, string Name, MepDiscipline Discipline, int ElementCount);

/// <summary>Текстовый параметр, по значению которого элементы делятся на системы.</summary>
/// <param name="Id">Идентификатор параметра: встроенного (отрицательный) или общего/проектного.</param>
/// <param name="Name">Имя параметра.</param>
/// <param name="Categories">Выбранные категории, по которым этим параметром можно фильтровать вид.</param>
public sealed record SystemParameterInfo(long Id, string Name, IReadOnlyList<BuiltInCategory> Categories);

/// <summary>Система, найденная на исходном виде.</summary>
/// <param name="Name">Имя системы.</param>
/// <param name="ElementCount">Сколько элементов исходного вида к ней относится.</param>
public sealed record SystemInfo(string Name, int ElementCount)
{
    /// <summary>
    ///     Меньше - похоже на обрывок системы: неподключённая муфта или кусок трубы, которые Ревит
    ///     вынес в отдельную систему. Самая маленькая настоящая система в моделях - около 14 элементов.
    /// </summary>
    public const int MinElements = 6;

    public bool IsFragment => ElementCount < MinElements;
}

/// <summary>Шаблон 3D-вида.</summary>
public sealed record ViewTemplateInfo(long Id, string Name);

/// <summary>Каким шаблоном оформить виды схем.</summary>
public enum TemplateMode
{
    /// <summary>Тем же, что у исходного вида (или без шаблона, если у него нет).</summary>
    AsSourceView,

    /// <summary>Без шаблона.</summary>
    None,

    /// <summary>Выбранным шаблоном.</summary>
    Specific
}

/// <summary>Что сделать: какие схемы и как оформить.</summary>
public sealed record SystemViewsRequest
{
    public required SystemParameterInfo Parameter { get; init; }

    public required IReadOnlyList<string> Systems { get; init; }

    /// <summary>Все выбранные категории, в том числе те, где параметра нет.</summary>
    public required IReadOnlyList<BuiltInCategory> Categories { get; init; }

    public required string ViewNamePrefix { get; init; }

    public required TemplateMode TemplateMode { get; init; }

    /// <summary>Шаблон для <see cref="TemplateMode.Specific" />.</summary>
    public long? TemplateId { get; init; }

    /// <summary>Подрезать 3D-рамку по элементам системы.</summary>
    public required bool FitSectionBox { get; init; }
}

/// <summary>Что получилось с одной схемой.</summary>
/// <param name="System">Система.</param>
/// <param name="ViewName">Имя вида.</param>
/// <param name="Created">Вид создан, а не обновлён существующий.</param>
/// <param name="Error">Причина, если схема не получилась.</param>
public sealed record SystemViewOutcome(string System, string ViewName, bool Created, string? Error = null)
{
    public bool Succeeded => Error is null;
}

/// <summary>Итог запуска.</summary>
public sealed record SystemViewsReport
{
    public required IReadOnlyList<SystemViewOutcome> Outcomes { get; init; }

    /// <summary>Категории без параметра системы: на схемах они скрыты целиком.</summary>
    public required IReadOnlyList<string> HiddenCategories { get; init; }

    /// <summary>
    ///     Базовый шаблон: из него сделан свой шаблон каждой схемы. Нет - схемы без шаблона.
    /// </summary>
    public string? BaseTemplate { get; init; }
}
