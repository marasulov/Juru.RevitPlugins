using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Juru.RevitPlugins.ClashNavigator.Models.RevitClashReport;
/// <summary>
/// Разбор узлов xml отчёта о коллизиях Navisworks, не зависящий от Revit API.
/// </summary>
/// <remarks>
/// Вынесен из <see cref="NavisXmlClashesLoader"/>, чтобы проверяться обычными тестами:
/// сам загрузчик сопоставляет объекты с элементами Revit и без Ревита не работает.
/// </remarks>
internal static class NavisXmlReport {
    /// <summary>Имена свойств, в которых отчёты Navisworks экспортируют Id элемента Revit.</summary>
    public static readonly string[] ElementIdNames = [
        "element id", "id объекта", "объект id", "id элемента", "идентификатор элемента",
        "elementid", "item id", "revit id", "id"
    ];

    /// <summary>Имена свойств с именем файла, из которого экспортирован объект.</summary>
    public static readonly string[] SourceFileNames = [
        "элемент файл источника", "файл источника", "имя файла",
        "item source file", "source file", "file name", "filename"
    ];

    /// <summary>Имена свойств с именем объекта.</summary>
    public static readonly string[] ObjectNameNames = [
        "item name", "имя элемента", "объект имя", "имя", "name"
    ];

    /// <summary>Имена свойств с типом или семейством объекта.</summary>
    public static readonly string[] ObjectTypeNames = [
        "item type", "тип элемента", "объект семейство", "семейство", "тип", "type"
    ];

    /// <summary>Имена свойств с уровнем объекта.</summary>
    public static readonly string[] LevelNames = ["level", "уровень", "layer", "слой"];

    /// <summary>
    /// Названия статусов коллизии в разных локализациях отчёта, приведённые
    /// к английскому статусу Navisworks из атрибута status.
    /// </summary>
    private static readonly Dictionary<string, string> _statuses =
        new(StringComparer.CurrentCultureIgnoreCase) {
            ["new"] = "active",
            ["active"] = "active",
            ["создать"] = "active",
            ["новый"] = "active",
            ["новая"] = "active",
            ["активно"] = "active",
            ["активный"] = "active",
            ["reviewed"] = "approved",
            ["approved"] = "approved",
            ["проверено"] = "approved",
            ["утверждено"] = "approved",
            ["resolved"] = "resolved",
            ["разрешено"] = "resolved",
            ["исправлено"] = "resolved"
        };

    /// <summary>
    /// Возвращает узлы отдельных коллизий проверки.
    /// </summary>
    /// <remarks>
    /// Сгруппированные коллизии Navisworks экспортирует как clashgroup: группа либо содержит
    /// вложенные clashresult, либо (когда включено объединение) несёт clashobjects
    /// сама - тогда узлом коллизии является сама группа.
    /// </remarks>
    public static IEnumerable<XElement> GetClashResults(XElement clashTest) {
        return clashTest.Descendants()
            .Where(node => node.Name == "clashresult"
                || (node.Name == "clashgroup" && !node.Descendants("clashresult").Any()));
    }

    /// <summary>
    /// Возвращает значение свойства с одним из заданных имён, найденное в любом узле
    /// поддерева объекта коллизии, или пустую строку.
    /// </summary>
    /// <remarks>
    /// Разные версии и локализации Navisworks пишут свойства и как objectattribute,
    /// и как smarttag, на самом узле объекта или на вложенных узлах, поэтому имя ищется
    /// без учёта регистра по всему поддереву. Имена в <paramref name="names" />
    /// перечислены по убыванию точности.
    /// </remarks>
    public static string GetPropertyValue(XElement clashObject, string[] names) {
        var properties = clashObject.Descendants()
            .Where(node => node.Name == "objectattribute" || node.Name == "smarttag")
            .Select(node => (
                Name: ((string) node.Element("name"))?.Trim() ?? string.Empty,
                Value: (string) node.Element("value")))
            .Where(property => !string.IsNullOrWhiteSpace(property.Value))
            .ToArray();
        return names
            .Select(name => properties.FirstOrDefault(property =>
                property.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase)).Value)
            .FirstOrDefault(value => value is not null)
            ?? string.Empty;
    }

    /// <summary>
    /// Возвращает уровень объекта коллизии: слой Navisworks, а если его нет - свойство уровня.
    /// </summary>
    public static string GetLevel(XElement clashObject) {
        return clashObject.Descendants("layer").FirstOrDefault()?.Value
            ?? GetPropertyValue(clashObject, LevelNames);
    }

    /// <summary>
    /// Возвращает статус коллизии в виде английского статуса Navisworks
    /// ("active", "approved", "resolved") или null, если статус не распознан.
    /// </summary>
    /// <remarks>
    /// Английский статус берётся из атрибута status, локализованный - из элемента
    /// resultstatus на языке Navisworks. Атрибут проверяется первым: он не зависит от языка.
    /// </remarks>
    public static string GetStatus(XElement clashResult) {
        string[] values = [
            clashResult.Attribute("status")?.Value,
            clashResult.Element("resultstatus")?.Value
        ];
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => _statuses.TryGetValue(value.Trim(), out string status) ? status : null)
            .FirstOrDefault(status => status is not null);
    }
}
