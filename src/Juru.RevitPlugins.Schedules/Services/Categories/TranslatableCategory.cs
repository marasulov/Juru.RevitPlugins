using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Schedules.Models;

namespace Juru.RevitPlugins.Schedules.Services.Categories;

/// <summary>
///     Общая часть всех категорий: отбор переводимого текста и сборка строки. Отбор один на все
///     категории — иначе в одной вкладке окажется текст, который в другой отсеян.
/// </summary>
public abstract class TranslatableCategory : ITranslatableCategory
{
    public abstract string Key { get; }

    public abstract string Name { get; }

    /// <summary>Что показать вместо таблицы, если в модели такого текста нет.</summary>
    public abstract string EmptyNote { get; }

    public abstract IReadOnlyList<ElementText> Collect(CollectScope scope);

    /// <summary>
    ///     Собирает строку по одной цели. <c>null</c> — переводить нечего: цель пуста, без кириллицы
    ///     или это подпись-распорка.
    /// </summary>
    /// <remarks>
    ///     У переведённого объекта в цели стоит английский текст, а русский лежит в самом объекте:
    ///     отбор идёт по сохранённому исходнику, иначе уже переведённая строка выпала бы из списка
    ///     и откатить её было бы нечем.
    /// </remarks>
    protected ElementText? Describe(Element element, TextTarget target, string context)
    {
        return Describe(element, target, context, TextTargetAccess.Read(element, target));
    }

    /// <summary>
    ///     То же, но текст известен заранее. Нужно там, где он не читается из элемента: описание
    ///     ключевого примечания лежит во внешнем файле.
    /// </summary>
    protected ElementText? Describe(Element element, TextTarget target, string context, string currentText)
    {
        var current = currentText.Trim();

        if (current.Length is 0)
        {
            return null;
        }

        var stored = SourceTextStore.TryRead(element, target);
        var source = stored ?? current;

        if (!RussianText.ContainsLetters(source) || RussianText.IsSpacerLabel(source))
        {
            return null;
        }

        return new ElementText
        {
            ElementId = element.Id,
            Category = Name,
            Context = context,
            Target = target,
            CurrentText = current,
            SourceText = source,
            IsTranslated = stored is not null
        };
    }

    protected IEnumerable<ElementText> DescribeAll(
        Element element,
        string context,
        params TextTarget[] targets)
    {
        return targets
            .Select(target => Describe(element, target, context))
            .Where(text => text is not null)
            .Select(text => text!);
    }

    /// <summary>Порядок один на все вкладки: сначала по месту, потом по исходному тексту.</summary>
    protected static IReadOnlyList<ElementText> Order(IEnumerable<ElementText> texts)
    {
        return texts
            .OrderBy(text => text.Context, System.StringComparer.CurrentCulture)
            .ThenBy(text => text.What, System.StringComparer.CurrentCulture)
            .ThenBy(text => text.SourceText, System.StringComparer.CurrentCulture)
            .ToList();
    }
}
