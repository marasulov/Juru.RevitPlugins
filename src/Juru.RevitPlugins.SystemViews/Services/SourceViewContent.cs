using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.SystemViews.Models;

namespace Juru.RevitPlugins.SystemViews.Services;

/// <summary>
///     Что окну нужно знать о модели: категории, параметры, системы, шаблоны.
/// </summary>
/// <remarks>
///     Окну отдаются только простые записи, без объектов Ревита: так модель окна не держит
///     элементы и не зависит от Revit API.
/// </remarks>
public interface IMepModelSource
{
    string SourceViewName { get; }

    IReadOnlyList<MepCategoryInfo> Categories { get; }

    IReadOnlyList<ViewTemplateInfo> Templates { get; }

    /// <summary>Имена существующих 3D-видов: по ним окно показывает, какие схемы обновятся.</summary>
    IReadOnlyCollection<string> ExistingViewNames { get; }

    /// <summary>Текстовые параметры, по которым можно фильтровать хотя бы одну из категорий.</summary>
    IReadOnlyList<SystemParameterInfo> FindParameters(IReadOnlyCollection<BuiltInCategory> categories);

    /// <summary>Системы, найденные в значениях параметра у элементов исходного вида.</summary>
    IReadOnlyList<SystemInfo> FindSystems(SystemParameterInfo parameter);
}

/// <summary>
///     Инженерные элементы исходного 3D-вида. Читаются один раз при открытии окна.
/// </summary>
/// <remarks>
///     Живёт только внутри команды: окно модальное, и вызовы из него идут в контексте Revit API.
/// </remarks>
public sealed class SourceViewContent : IMepModelSource
{
    private readonly Document document;
    private readonly Dictionary<BuiltInCategory, List<Element>> elementsByCategory;

    public SourceViewContent(Document document, View3D sourceView)
    {
        this.document = document ?? throw new ArgumentNullException(nameof(document));
        SourceView = sourceView ?? throw new ArgumentNullException(nameof(sourceView));

        var known = MepCategories.All.Select(item => item.Category).ToList();

        elementsByCategory = new FilteredElementCollector(document, sourceView.Id)
            .WherePasses(new ElementMulticategoryFilter(known))
            .WhereElementIsNotElementType()
            .ToElements()
            .GroupBy(element => (BuiltInCategory) ElementIds.ToLong(element.Category.Id))
            .ToDictionary(group => group.Key, group => group.ToList());

        Categories = MepCategories.All
            .Select(item => (item, category: Category.GetCategory(document, item.Category)))
            .Where(pair => pair.category is not null)
            .Select(pair => new MepCategoryInfo(
                pair.item.Category,
                pair.category!.Name,
                pair.item.Discipline,
                elementsByCategory.TryGetValue(pair.item.Category, out var list) ? list.Count : 0))
            .ToList();

        Templates = new FilteredElementCollector(document)
            .OfClass(typeof(View3D))
            .Cast<View3D>()
            .Where(view => view.IsTemplate)
            .Select(view => new ViewTemplateInfo(ElementIds.ToLong(view.Id), view.Name))
            .OrderBy(template => template.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        ExistingViewNames = new FilteredElementCollector(document)
            .OfClass(typeof(View3D))
            .Cast<View3D>()
            .Where(view => !view.IsTemplate)
            .Select(view => view.Name)
            .ToHashSet(StringComparer.CurrentCulture);
    }

    public View3D SourceView { get; }

    public string SourceViewName => SourceView.Name;

    public IReadOnlyList<MepCategoryInfo> Categories { get; }

    public IReadOnlyList<ViewTemplateInfo> Templates { get; }

    public IReadOnlyCollection<string> ExistingViewNames { get; }

    public int ElementCount => elementsByCategory.Values.Sum(list => list.Count);

    public IReadOnlyList<SystemParameterInfo> FindParameters(IReadOnlyCollection<BuiltInCategory> categories)
    {
        var names = new Dictionary<long, string>();
        var applicable = new Dictionary<long, List<BuiltInCategory>>();

        foreach (var category in categories)
        {
            var filterable = ParameterFilterUtilities
                .GetFilterableParametersInCommon(document, [new ElementId(category)])
                .Select(ElementIds.ToLong)
                .ToHashSet();

            foreach (var id in filterable)
            {
                if (!applicable.TryGetValue(id, out var list))
                {
                    applicable[id] = list = [];
                }

                list.Add(category);
            }

            // Тип хранения и имя берутся у живого элемента: по одному id их не узнать для встроенных
            // параметров. Поэтому параметры категорий, которых на виде нет, в список не попадают -
            // предлагать параметр, по которому нечего делить, незачем.
            if (!elementsByCategory.TryGetValue(category, out var elements) || elements.Count is 0)
            {
                continue;
            }

            var sample = elements[0];
            var type = document.GetElement(sample.GetTypeId());

            foreach (var parameter in Parameters(sample).Concat(type is null ? [] : Parameters(type)))
            {
                var id = ElementIds.ToLong(parameter.Id);

                if (parameter.StorageType is StorageType.String && filterable.Contains(id) && !names.ContainsKey(id))
                {
                    names[id] = parameter.Definition.Name;
                }
            }
        }

        return names
            .Select(pair => new SystemParameterInfo(pair.Key, pair.Value, applicable[pair.Key]))
            .OrderBy(parameter => parameter.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<SystemInfo> FindSystems(SystemParameterInfo parameter)
    {
        var values = Values(parameter).ToList();

        var names = values
            .SelectMany(item => SystemNames.Split(item.Value))
            .Distinct(SystemNames.Comparer)
            .ToList();

        return names
            .Select(name => new SystemInfo(name, values.Count(item => SystemNames.IsMatch(item.Value, name))))
            .Where(system => system.ElementCount > 0)
            .OrderBy(system => system.Name, NaturalComparer.Instance)
            .ToList();
    }

    /// <summary>Элементы исходного вида, относящиеся к системе.</summary>
    public IReadOnlyList<Element> ElementsOf(SystemParameterInfo parameter, string system)
    {
        return Values(parameter)
            .Where(item => SystemNames.IsMatch(item.Value, system))
            .Select(item => item.Element)
            .ToList();
    }

    private IEnumerable<(Element Element, string? Value)> Values(SystemParameterInfo parameter)
    {
        var lookup = new ParameterLookup(document, ElementIds.FromLong(parameter.Id));

        foreach (var category in parameter.Categories)
        {
            if (!elementsByCategory.TryGetValue(category, out var elements))
            {
                continue;
            }

            foreach (var element in elements)
            {
                yield return (element, lookup.Find(element)?.AsString());
            }
        }
    }

    private static IEnumerable<Parameter> Parameters(Element element)
    {
        foreach (Parameter parameter in element.Parameters)
        {
            yield return parameter;
        }
    }
}

/// <summary>
///     Находит параметр у элемента по id: встроенный - по <see cref="BuiltInParameter" />, общий - по
///     GUID, проектный - по определению. Не нашёлся у экземпляра - ищется у типа: фильтры вида
///     проверяют и параметры типа.
/// </summary>
internal sealed class ParameterLookup
{
    private readonly Document document;
    private readonly Func<Element, Parameter?> find;

    public ParameterLookup(Document document, ElementId id)
    {
        this.document = document;

        var value = ElementIds.ToLong(id);

        if (value < 0)
        {
            var builtIn = (BuiltInParameter) value;
            find = element => element.get_Parameter(builtIn);
        }
        else if (document.GetElement(id) is SharedParameterElement shared)
        {
            var guid = shared.GuidValue;
            find = element => element.get_Parameter(guid);
        }
        else if (document.GetElement(id) is ParameterElement projectParameter)
        {
            var definition = projectParameter.GetDefinition();
            find = element => element.get_Parameter(definition);
        }
        else
        {
            find = _ => null;
        }
    }

    public Parameter? Find(Element element)
    {
        return find(element) ?? (document.GetElement(element.GetTypeId()) is { } type ? find(type) : null);
    }
}

/// <summary>Сравнение «В2» раньше «В10»: числа внутри имён сравниваются как числа.</summary>
internal sealed class NaturalComparer : IComparer<string>
{
    public static NaturalComparer Instance { get; } = new();

    public int Compare(string? left, string? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        int i = 0, j = 0;

        while (i < left.Length && j < right.Length)
        {
            if (char.IsDigit(left[i]) && char.IsDigit(right[j]))
            {
                var startLeft = i;
                var startRight = j;

                while (i < left.Length && char.IsDigit(left[i])) i++;
                while (j < right.Length && char.IsDigit(right[j])) j++;

                var numberLeft = left.Substring(startLeft, i - startLeft).TrimStart('0');
                var numberRight = right.Substring(startRight, j - startRight).TrimStart('0');

                var byLength = numberLeft.Length.CompareTo(numberRight.Length);

                if (byLength != 0)
                {
                    return byLength;
                }

                var byDigits = string.CompareOrdinal(numberLeft, numberRight);

                if (byDigits != 0)
                {
                    return byDigits;
                }

                continue;
            }

            var byChar = string.Compare(left[i].ToString(), right[j].ToString(), StringComparison.CurrentCultureIgnoreCase);

            if (byChar != 0)
            {
                return byChar;
            }

            i++;
            j++;
        }

        return (left.Length - i).CompareTo(right.Length - j);
    }
}
