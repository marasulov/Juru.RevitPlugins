using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace Juru.RevitPlugins.ClashNavigator.Services.NavisworksImport;
/// <summary>
/// Условие поиска Navisworks: вкладка, свойство, проверка и значение.
/// </summary>
/// <param name="CategoryInternal">Внутреннее имя вкладки свойств, например <c>LcOaNode</c>.</param>
/// <param name="CategoryName">Видимое имя вкладки, например <c>Item</c>.</param>
/// <param name="PropertyInternal">Внутреннее имя свойства, например <c>lcldrevit_parameter_-1140238</c>.</param>
/// <param name="PropertyName">Видимое имя свойства, например <c>Внешний диаметр</c>.</param>
/// <param name="Test">Проверка: <c>equals</c>, <c>contains</c>, <c>wildcard</c>, <c>greater_than</c> и т.д.</param>
/// <param name="DataType">Тип значения: <c>wstring</c>, <c>linear</c>, <c>name</c> и т.д.</param>
/// <param name="Value">Значение как записано в файле; для <c>name</c> - видимое имя.</param>
internal sealed record NavisCondition(
    string CategoryInternal,
    string CategoryName,
    string PropertyInternal,
    string PropertyName,
    string Test,
    string DataType,
    string Value) {

    public override string ToString() {
        return $"{CategoryName}/{PropertyName} {Test} {Value}".Trim();
    }
}

/// <summary>
/// Поисковый набор Navisworks.
/// </summary>
/// <remarks>
/// Условия Navisworks идут подряд; условие с флагом "ИЛИ" (бит 64) начинает новую группу.
/// Внутри группы условия складываются через "И", группы - через "ИЛИ".
/// </remarks>
internal sealed class NavisSearchSet {
    public string Name { get; init; }

    /// <summary>
    /// Путь в дереве наборов, например <c>ARC/ARC_Walls</c> - так на набор ссылаются тесты.
    /// </summary>
    public string Path { get; init; }

    /// <summary>
    /// Набор задан явным выбором элементов, а не поиском, - перевести его в фильтр нельзя.
    /// </summary>
    public bool IsExplicitSelection { get; init; }

    public IReadOnlyList<IReadOnlyList<NavisCondition>> Groups { get; init; } = [];
}

/// <summary>
/// Тест Clash Detective: имя и наборы слева и справа.
/// </summary>
internal sealed class NavisClashTest {
    public string Name { get; init; }

    /// <summary>
    /// Путь набора слева; <see langword="null" />, если слева ничего не выбрано.
    /// </summary>
    public string Left { get; init; }

    /// <summary>
    /// Путь набора справа; <see langword="null" />, если справа ничего не выбрано.
    /// </summary>
    public string Right { get; init; }

    /// <summary>
    /// Слева или справа указано не поисковое множество, а дерево модели или явный выбор.
    /// </summary>
    public string UnsupportedLocator { get; init; }

    /// <summary>
    /// Допуск теста в единицах файла (<c>units</c>).
    /// </summary>
    public double Tolerance { get; init; }
}

/// <summary>
/// Содержимое файла обмена Navisworks (<c>exchange</c>): поисковые наборы, тесты или и то и другое.
/// </summary>
internal sealed class NavisExchange {
    private const string _selectionSetPrefix = "lcop_selection_set_tree/";
    private const int _orFlag = 64;

    /// <summary>
    /// Единицы длины значений в файле (<c>units</c> у корня): <c>ft</c>, <c>m</c>, <c>mm</c> и т.д.
    /// </summary>
    public string Units { get; private init; }

    public IReadOnlyList<NavisSearchSet> SearchSets { get; private init; } = [];
    public IReadOnlyList<NavisClashTest> ClashTests { get; private init; } = [];

    public static bool IsNavisworksExchange(XDocument document) {
        return document.Root?.Name.LocalName == "exchange";
    }

    public static NavisExchange Parse(XDocument document) {
        if(document is null) { throw new ArgumentNullException(nameof(document)); }
        if(!IsNavisworksExchange(document)) {
            throw new FormatException("Это не файл обмена Navisworks: корневой элемент не exchange.");
        }

        var root = document.Root;
        return new NavisExchange {
            Units = (string) root.Attribute("units") ?? "ft",
            SearchSets = root.Elements("selectionsets")
                .SelectMany(item => ReadSets(item, string.Empty))
                .ToArray(),
            ClashTests = root.Elements("batchtest")
                .Elements("clashtests")
                .Elements("clashtest")
                .Select(ReadTest)
                .ToArray()
        };
    }

    private static IEnumerable<NavisSearchSet> ReadSets(XElement container, string parentPath) {
        foreach(var element in container.Elements()) {
            string name = (string) element.Attribute("name") ?? string.Empty;
            string path = string.IsNullOrEmpty(parentPath) ? name : parentPath + "/" + name;
            if(element.Name.LocalName == "viewfolder") {
                foreach(var set in ReadSets(element, path)) {
                    yield return set;
                }
            } else if(element.Name.LocalName == "selectionset") {
                yield return ReadSet(element, name, path);
            }
        }
    }

    private static NavisSearchSet ReadSet(XElement element, string name, string path) {
        var findSpec = element.Element("findspec");
        if(findSpec is null) {
            return new NavisSearchSet { Name = name, Path = path, IsExplicitSelection = true };
        }

        List<IReadOnlyList<NavisCondition>> groups = [];
        List<NavisCondition> current = [];
        foreach(var condition in findSpec.Elements("conditions").Elements("condition")) {
            int.TryParse((string) condition.Attribute("flags"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int flags);
            if((flags & _orFlag) != 0 && current.Count > 0) {
                groups.Add(current);
                current = [];
            }
            current.Add(ReadCondition(condition));
        }
        if(current.Count > 0) {
            groups.Add(current);
        }

        return new NavisSearchSet { Name = name, Path = path, Groups = groups };
    }

    private static NavisCondition ReadCondition(XElement condition) {
        var category = condition.Element("category")?.Element("name");
        var property = condition.Element("property")?.Element("name");
        var data = condition.Element("value")?.Element("data");
        string value = data?.Element("name") is { } named
            ? named.Value
            : data?.Value;

        return new NavisCondition(
            (string) category?.Attribute("internal") ?? string.Empty,
            category?.Value ?? string.Empty,
            (string) property?.Attribute("internal") ?? string.Empty,
            property?.Value ?? string.Empty,
            (string) condition.Attribute("test") ?? string.Empty,
            (string) data?.Attribute("type") ?? string.Empty,
            value?.Trim());
    }

    private static NavisClashTest ReadTest(XElement test) {
        string unsupported = null;
        string ReadSide(string side) {
            var locator = test.Element(side)?.Element("clashselection")?.Element("locator")?.Value?.Trim();
            if(string.IsNullOrEmpty(locator)) {
                return null;
            }
            if(locator.StartsWith(_selectionSetPrefix, StringComparison.Ordinal)) {
                return locator.Substring(_selectionSetPrefix.Length);
            }
            unsupported ??= locator;
            return null;
        }

        string left = ReadSide("left");
        string right = ReadSide("right");
        double.TryParse((string) test.Attribute("tolerance"), NumberStyles.Float, CultureInfo.InvariantCulture, out double tolerance);
        return new NavisClashTest {
            Tolerance = tolerance,
            Name = (string) test.Attribute("name") ?? string.Empty,
            Left = left,
            Right = right,
            UnsupportedLocator = unsupported
        };
    }
}
