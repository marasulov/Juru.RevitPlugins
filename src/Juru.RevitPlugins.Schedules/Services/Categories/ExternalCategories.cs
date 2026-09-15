using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Schedules.Models;

using Juru.RevitPlugins.Schedules.Localization;

namespace Juru.RevitPlugins.Schedules.Services.Categories;

/// <summary>
///     Значения параметров рабочих элементов модели: описания, марки и прочий текст, который печатают
///     спецификации, но который не покрыт отдельной вкладкой (помещения, зоны, виды, листы, материалы).
///     Переводится значение, а не имя параметра — определение общее на проект, а параметры, по которым
///     спецификации сортируют, группируют или находят элемент, защищены и не трогаются вовсе:
///     см. <see cref="ElementParameterSafety" />.
/// </summary>
public sealed class GlobalParameters : TranslatableCategory
{
    public override string Key
    {
        get { return "GlobalParameters"; }
    }

    public override string Name
    {
        get { return Text.CategoryGlobalParameters; }
    }

    public override string EmptyNote
    {
        get { return Text.EmptyGlobalParameters; }
    }

    public override IReadOnlyList<ElementText> Collect(CollectScope scope)
    {
        var elements = ElementParameterSafety.Categories.SelectMany(scope.Instances);
        var texts = elements.SelectMany(DescribeElement);

        return Order(Deduplicate(texts));
    }

    private IEnumerable<ElementText> DescribeElement(Element element)
    {
        var context = element.Category?.Name ?? Text.TargetGlobalParameter;

        return DescribeAll(element, context, ElementParameterSafety.ResolveTargets(element).ToArray());
    }

    /// <summary>
    ///     Один и тот же типоразмер обычно встречается десятками и сотнями экземпляров с одинаковым
    ///     текстом в одном параметре — вентиляция и трубопроводы этим особенно грешат. Без свёртки
    ///     список превращается в тысячи одинаковых строк, а окно виснет на выделении такого количества.
    ///     В список попадает одна строка, а перевод при записи расходится по всем найденным дубликатам.
    /// </summary>
    private static IEnumerable<ElementText> Deduplicate(IEnumerable<ElementText> texts)
    {
        foreach (var group in texts.GroupBy(text => (text.Context, text.Target, text.SourceText)))
        {
            var duplicates = group.Skip(1).Select(text => text.ElementId).ToList();
            var primary = group.First();

            yield return duplicates.Count is 0 ? primary : primary with { DuplicateElementIds = duplicates };
        }
    }
}

/// <summary>
///     Описания в таблице ключевых примечаний.
/// </summary>
/// <remarks>
///     Таблица лежит во внешнем файле, а не в элементах модели, поэтому перевод пишется не в элементы:
///     рядом с исходным файлом создаётся <c>keynotes_EN.txt</c>, и модель переключается на него.
///     Русский файл остаётся цел — он общий для всех моделей, которые на него ссылаются.
/// </remarks>
public sealed class Keynotes : TranslatableCategory
{
    public override string Key
    {
        get { return "Keynotes"; }
    }

    public override string Name
    {
        get { return Text.CategoryKeynotes; }
    }

    public override string EmptyNote
    {
        get
        {
            return Text.EmptyKeynotes;
        }
    }

    public override IReadOnlyList<ElementText> Collect(CollectScope scope)
    {
        var file = KeynoteFile.TryRead(scope.Document);

        if (file is null)
        {
            return [];
        }

        // Элемент один на всю таблицу: у отдельной записи своего элемента в модели нет, а показать
        // строку в модели всё равно нельзя — она в файле.
        var table = KeynoteTable.GetKeynoteTable(scope.Document);

        return Order(file.Entries
            .Select(entry => Describe(table, TextTarget.KeynoteEntry(entry.Key), entry.Key, entry.Description))
            .Where(text => text is not null)
            .Select(text => text!));
    }
}
