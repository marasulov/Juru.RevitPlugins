using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

using Autodesk.Revit.DB;

using dosymep.Bim4Everyone;
using dosymep.Bim4Everyone.SystemParams;
using dosymep.SimpleServices;

using Juru.RevitPlugins.ClashNavigator.Models;
using Juru.RevitPlugins.ClashNavigator.Models.Evaluators;
using Juru.RevitPlugins.ClashNavigator.Models.FilterModel;
using Juru.RevitPlugins.ClashNavigator.Models.Value;

using ParameterValueProvider = Juru.RevitPlugins.ClashNavigator.Models.FilterableValueProviders.ParameterValueProvider;

namespace Juru.RevitPlugins.ClashNavigator.Services.NavisworksImport;
/// <summary>
/// Выгружает поисковые наборы и проверки плагина в файлы обмена Navisworks - обратное
/// <see cref="NavisworksImporter" />.
/// </summary>
/// <remarks>
/// Набор плагина - "категории И правила". В Navisworks это ИЛИ-группы: по группе на каждую
/// категорию (и на каждую ветку ИЛИ в правилах, и на каждый шаблон исходного файла), в группе -
/// категория и правила через И. Значения пишутся в футах (<c>units="ft"</c>), как их хранит Ревит.
/// Встроенный параметр пишется по id (<c>lcldrevit_parameter_-1140238</c>), остальные - только
/// видимым именем: внутреннее имя общего параметра у Navisworks содержит группу, которой у
/// плагина нет, а по видимому имени Navisworks ищет тоже.
/// </remarks>
internal sealed class NavisworksExporter {
    private const string _schema = "http://download.autodesk.com/us/navisworks/schemas/nw-exchange-12.0.xsd";
    private const int _orFlag = 64;
    private static readonly XNamespace _xsi = "http://www.w3.org/2001/XMLSchema-instance";

    private readonly Document _doc;
    private readonly ILocalizationService _localization;

    public NavisworksExporter(Document doc, ILocalizationService localization) {
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
    }

    public XDocument ExportSearchSets(IEnumerable<Filter> filters, NavisworksImportReport report) {
        var sets = new XElement("selectionsets");
        foreach(var filter in filters) {
            var groups = BuildGroups(filter, report);
            if(groups.Count == 0) {
                report.Skip(filter.Name, Text("Export.NoCategories"));
                continue;
            }

            var conditions = new XElement("conditions");
            foreach(var group in groups) {
                for(int i = 0; i < group.Count; i++) {
                    var condition = group[i];
                    // Первое условие группы, кроме самой первой, начинает ИЛИ.
                    if(i == 0 && conditions.HasElements) {
                        condition.SetAttributeValue("flags", _orFlag);
                    }
                    conditions.Add(condition);
                }
            }

            sets.Add(new XElement("selectionset",
                new XAttribute("name", filter.Name),
                new XAttribute("guid", Guid.NewGuid()),
                new XElement("findspec",
                    new XAttribute("mode", "all"),
                    new XAttribute("disjoint", 0),
                    conditions,
                    new XElement("locator", "/"))));
        }
        return CreateExchange(sets);
    }

    /// <summary>
    /// Тесты Clash Detective по проверкам. Наборы тест ищет по имени в корне дерева наборов -
    /// так их кладёт <see cref="ExportSearchSets" />.
    /// </summary>
    public XDocument ExportClashTests(IEnumerable<Check> checks, string batchName, NavisworksImportReport report) {
        var tests = new XElement("clashtests");
        foreach(var check in checks) {
            string left = check.FirstSelection?.Filters.FirstOrDefault();
            string right = check.SecondSelection?.Filters.FirstOrDefault();
            if(left is null || right is null) {
                report.Skip(check.Name, Text("Export.CheckNoSets"));
                continue;
            }
            if(check.FirstSelection.Filters.Count > 1 || check.SecondSelection.Filters.Count > 1) {
                report.Note(check.Name, Text("Export.CheckFirstSetOnly"));
            }

            tests.Add(new XElement("clashtest",
                new XAttribute("name", check.Name),
                new XAttribute("test_type", "hard"),
                new XAttribute("status", "new"),
                new XAttribute("tolerance", Format(check.Tolerance / 304.8)),
                new XAttribute("merge_composites", 0),
                new XElement("linkage", new XAttribute("mode", "none")),
                Side("left", left),
                Side("right", right),
                new XElement("rules")));
        }

        return CreateExchange(new XElement("batchtest",
            new XAttribute("name", batchName),
            new XAttribute("internal_name", batchName),
            new XAttribute("units", "ft"),
            tests));
    }

    private static XElement Side(string side, string setName) {
        return new XElement(side,
            new XElement("clashselection",
                new XAttribute("selfintersect", 0),
                new XAttribute("primtypes", 1),
                new XElement("locator", "lcop_selection_set_tree/" + setName)));
    }

    private static XDocument CreateExchange(XElement content) {
        return new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement("exchange",
                new XAttribute(XNamespace.Xmlns + "xsi", _xsi),
                new XAttribute(_xsi + "noNamespaceSchemaLocation", _schema),
                new XAttribute("units", "ft"),
                new XAttribute("filename", string.Empty),
                new XAttribute("filepath", string.Empty),
                content));
    }

    /// <remarks>
    /// Группа "категория И правила" пишется, только если встроенные параметры правил применимы
    /// к категории: иначе Navisworks по ней ничего не найдёт, а у плагина элементы этой
    /// категории правило и так не проходят. Без этого обратный импорт видел группу с
    /// неприменимым параметром и отбрасывал её.
    /// </remarks>
    private List<List<XElement>> BuildGroups(Filter filter, NavisworksImportReport report) {
        var categories = (filter.CategoryIds ?? [])
            .Select(id => Category.GetCategory(_doc, id))
            .Where(category => !string.IsNullOrEmpty(category?.Name))
            .GroupBy(category => category.Name)
            .Select(group => group.First())
            .ToList();
        if(categories.Count == 0) {
            return [];
        }

        var ruleBranches = GetRuleBranches(filter, report);
        List<string> patterns = filter.SourceFilePatterns is { Count: > 0 } list ? list : [null];

        List<List<XElement>> groups = [];
        foreach(string pattern in patterns) {
            foreach(var category in categories) {
                var filterable = new HashSet<ElementId>(
                    ParameterFilterUtilities.GetFilterableParametersInCommon(_doc, [category.Id]));
                foreach(var rules in ruleBranches.Where(branch => branch.All(rule => IsApplicable(rule, filterable)))) {
                    List<XElement> group = [];
                    if(pattern is not null) {
                        group.Add(Condition("LcOaNode", "Item", "LcOaNodeSourceFile", "Source File",
                            "wildcard", "wstring", pattern));
                    }
                    group.Add(Condition("LcRevitData_Element", "Element", "LcRevitPropertyElementCategory", "Category",
                        "equals", "wstring", category.Name));
                    group.AddRange(rules.Select(rule => ConvertRule(filter, rule, report)).Where(item => item is not null));
                    groups.Add(group);
                }
            }
        }
        return groups;
    }

    private static bool IsApplicable(Rule rule, HashSet<ElementId> filterable) {
        return rule.Provider is not ParameterValueProvider { RevitParam: SystemParam systemParam }
               || filterable.Contains(new ElementId(systemParam.SystemParamId));
    }

    /// <summary>
    /// Ветки ИЛИ из дерева правил; в каждой - условия через И.
    /// </summary>
    /// <remarks>
    /// Navisworks знает только один уровень: И внутри группы, ИЛИ между группами. Поэтому
    /// переводятся "И правил", "ИЛИ правил" и "ИЛИ из И-наборов" - то, что даёт импорт и
    /// обычное окно наборов. Более глубокое дерево выгружается без правил, с записью в отчёт.
    /// </remarks>
    private List<List<Rule>> GetRuleBranches(Filter filter, NavisworksImportReport report) {
        var set = filter.Set;
        if(set is null || set.Criteria.Count == 0) {
            return [[]];
        }

        bool isOr = set.SetEvaluator?.Evaluator == SetEvaluators.Or;
        if(!isOr && set.Criteria.All(item => item is Rule)) {
            return [[.. set.Criteria.Cast<Rule>()]];
        }

        if(isOr && set.Criteria.All(item => item is Rule
               || item is Set { SetEvaluator.Evaluator: SetEvaluators.And } inner && inner.Criteria.All(c => c is Rule))) {
            return [.. set.Criteria.Select(item => item is Rule rule
                ? [rule]
                : ((Set) item).Criteria.Cast<Rule>().ToList())];
        }

        report.Note(filter.Name, Text("Export.SetTooDeep"));
        return [[]];
    }

    private XElement ConvertRule(Filter filter, Rule rule, NavisworksImportReport report) {
        if(rule.Provider is not ParameterValueProvider provider || provider.RevitParam is null) {
            // Псевдопараметр (рабочий набор) - не параметр Ревита, и как он называется в свойствах
            // Navisworks, не известно наверняка. Правило пропускается, но не молча: без него
            // набор в Navisworks отберёт больше, чем в Ревите.
            if(rule.Provider is not null) {
                report.Note(filter.Name, Text("Export.RuleSkipped", rule.Provider.Name, rule.Evaluator?.Evaluator));
            }
            return null;
        }

        string test = rule.Evaluator?.Evaluator switch {
            RuleEvaluators.FilterStringEquals or RuleEvaluators.FilterNumericEquals => "equals",
            RuleEvaluators.FilterNotEquals => "not_equals",
            RuleEvaluators.FilterStringContains => "contains",
            RuleEvaluators.FilterStringBeginsWith or RuleEvaluators.FilterStringEndsWith => "wildcard",
            RuleEvaluators.FilterStringGreater or RuleEvaluators.FilterNumericGreater => "greater_than",
            RuleEvaluators.FilterStringGreaterOrEqual or RuleEvaluators.FilterNumericGreaterOrEqual => "greater_equal",
            RuleEvaluators.FilterStringLess or RuleEvaluators.FilterNumericLess => "less_than",
            RuleEvaluators.FilterStringLessOrEqual or RuleEvaluators.FilterNumericLessOrEqual => "less_equal",
            RuleEvaluators.FilterHasValue => "defined",
            RuleEvaluators.FilterHasNoValue => "undefined",
            _ => null
        };
        if(test is null) {
            report.Note(filter.Name, Text("Export.RuleSkipped", provider.Name, rule.Evaluator?.Evaluator));
            return null;
        }

        var revitParam = provider.RevitParam;
        string internalName = revitParam is SystemParam systemParam
            ? "lcldrevit_parameter_" + Convert.ToInt64(systemParam.SystemParamId).ToString(CultureInfo.InvariantCulture)
            : null;

        if(test is "defined" or "undefined") {
            return Condition("LcRevitData_Element", "Element", internalName, revitParam.Name, test, null, null);
        }

        (string type, string value) = GetValue(revitParam, rule.Value);
        if(value is null) {
            report.Note(filter.Name, Text("Export.RuleSkipped", provider.Name, rule.Evaluator.Evaluator));
            return null;
        }
        if(test == "wildcard") {
            value = rule.Evaluator.Evaluator == RuleEvaluators.FilterStringBeginsWith ? value + "*" : "*" + value;
        }
        return Condition("LcRevitData_Element", "Element", internalName, revitParam.Name, test, type, value);
    }

    private static (string Type, string Value) GetValue(RevitParam revitParam, ParamValue value) {
        switch(value) {
            case DoubleParamValue number:
                return (GetDoubleType(revitParam), Format(number.TValue));
            case IntParamValue number:
                return ("int32", number.TValue.ToString(CultureInfo.InvariantCulture));
            case StringParamValue text:
                return ("wstring", text.TValue);
            case ElementIdParamValue name:
                return ("wstring", name.TValue);
            default:
                return (null, null);
        }
    }

    private static string GetDoubleType(RevitParam revitParam) {
        try {
            var spec = revitParam.UnitType;
            if(spec == SpecTypeId.Length) {
                return "linear";
            }
            if(spec == SpecTypeId.Area) {
                return "area";
            }
            if(spec == SpecTypeId.Volume) {
                return "volume";
            }
        } catch(Exception) {
            // Единицы неизвестны - пишем просто число.
        }
        return "double";
    }

    private static XElement Condition(
        string categoryInternal,
        string categoryName,
        string propertyInternal,
        string propertyName,
        string test,
        string dataType,
        string value) {

        var property = new XElement("name", propertyName);
        if(propertyInternal is not null) {
            property.SetAttributeValue("internal", propertyInternal);
        }

        var condition = new XElement("condition",
            new XAttribute("test", test),
            new XAttribute("flags", 0),
            new XElement("category", new XElement("name", new XAttribute("internal", categoryInternal), categoryName)),
            new XElement("property", property));
        if(dataType is not null) {
            condition.Add(new XElement("value", new XElement("data", new XAttribute("type", dataType), value)));
        }
        return condition;
    }

    private static string Format(double value) {
        return value.ToString("0.##########", CultureInfo.InvariantCulture);
    }

    private string Text(string key, params object[] args) {
        return _localization.GetLocalizedString(key, args);
    }
}
