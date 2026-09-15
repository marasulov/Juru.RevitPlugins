using System.Collections.Generic;

using Autodesk.Revit.DB;

namespace Juru.RevitPlugins.Schedules.Models;

/// <summary>
///     Один переводимый текст элемента модели. В отличие от спецификации, которая перерисовывается заново,
///     здесь перевод пишется в сам параметр, поэтому русская строка должна быть где-то сохранена — иначе
///     после записи откатить перевод нечем.
/// </summary>
public sealed record ElementText
{
    public required ElementId ElementId { get; init; }

    /// <summary>
    ///     Другие элементы с тем же значением в той же цели — например, десятки одинаковых фитингов
    ///     одного типоразмера. Строка в списке одна на всех, а перевод при записи расходится по каждому:
    ///     без этого категория с массовыми инженерными элементами превращает список в тысячи одинаковых
    ///     строк и вешает окно на выделении.
    /// </summary>
    public IReadOnlyList<ElementId> DuplicateElementIds { get; init; } = [];

    /// <summary>Категория в терминах окна: «Помещения», «Листы». По ней строка попадает на свою вкладку.</summary>
    public required string Category { get; init; }

    /// <summary>
    ///     Где искать строку в модели: номер помещения, номер листа. Без этого одинаковые названия
    ///     в списке неразличимы.
    /// </summary>
    public required string Context { get; init; }

    /// <summary>Что это за текст у объекта: «Имя», «Отдел», «Заголовок: Марка».</summary>
    public string What
    {
        get { return Target.Caption; }
    }

    /// <summary>Куда текст записывается: параметр, имя, текст примечания, заголовок колонки.</summary>
    public required TextTarget Target { get; init; }

    /// <summary>Что в параметре написано сейчас: после записи перевода — уже английская строка.</summary>
    public required string CurrentText { get; init; }

    /// <summary>Русский исходник: сохранённый при первой записи, а до неё — текущее значение параметра.</summary>
    public required string SourceText { get; init; }

    /// <summary>Перевод уже записан в модель, и русская строка лежит в элементе.</summary>
    public required bool IsTranslated { get; init; }
}
