using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

using Autodesk.Revit.DB;

using dosymep.Bim4Everyone;
using dosymep.Bim4Everyone.SystemParams;
using dosymep.Revit;
using dosymep.SimpleServices;

using Juru.RevitPlugins.ClashNavigator.Models;
using Juru.RevitPlugins.ClashNavigator.Models.Evaluators;
using Juru.RevitPlugins.ClashNavigator.Models.FilterableValueProviders;
using Juru.RevitPlugins.ClashNavigator.Models.FilterModel;
using Juru.RevitPlugins.ClashNavigator.Models.Value;

using ParameterValueProvider = Juru.RevitPlugins.ClashNavigator.Models.FilterableValueProviders.ParameterValueProvider;

namespace Juru.RevitPlugins.ClashNavigator.Services.NavisworksImport;
/// <summary>
/// Переводит поисковые наборы и тесты Navisworks в поисковые наборы и проверки плагина.
/// </summary>
/// <remarks>
/// Перевод приблизительный по природе: Navisworks ищет по дереву своей сцены и свойствам
/// любых форматов, плагин - фильтрами Ревита по категориям и параметрам. Что перевести нельзя,
/// пропускается и попадает в <see cref="NavisworksImportReport" />, а не роняет весь импорт.
/// <list type="bullet">
/// <item>Категория берётся из "Элемент/Тип", "Revit/Категория" и "Элемент/Имя" - по имени
/// категории в языке Ревита или по имени <see cref="BuiltInCategory" /> (английскому): наборы часто
/// содержат ИЛИ-группы на двух языках, и так срабатывает подходящая.</item>
/// <item>"Элемент/Исходный файл" сохраняется шаблоном в <see cref="Filter.SourceFilePatterns" />:
/// в фильтр Ревита не переводится, но задаёт файлы проверок из тестов.</item>
/// <item>Параметры Ревита - по встроенному id (<c>lcldrevit_parameter_-1140238</c>), иначе по имени
/// параметра проекта, общего параметра или подписи встроенного.</item>
/// </list>
/// </remarks>
internal sealed class NavisworksImporter {
    private const string _builtInParameterPrefix = "lcldrevit_parameter_";

    private readonly RevitRepository _revitRepository;
    private readonly ILocalizationService _localization;
    private readonly Document _doc;
    private readonly List<CategoryEntry> _categories;
    private Dictionary<string, List<BuiltInParameter>> _builtInParametersByLabel;
    private readonly Dictionary<string, HashSet<ElementId>> _filterableByCategory = [];

    public NavisworksImporter(RevitRepository revitRepository, ILocalizationService localization) {
        _revitRepository = revitRepository ?? throw new ArgumentNullException(nameof(revitRepository));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _doc = revitRepository.Doc;
        // Английские имена BuiltInCategory нужны, только если Ревит не английский: в английском
        // имя категории и так английское, а имя перечисления бывает шире ("Curtain Panels" -
        // OST_CurtainWallPanels), и "Тип содержит Wall" захватил бы лишнее.
        bool addBuiltInNames = !revitRepository.UiApplication.Application.Language.ToString()
            .StartsWith("English", StringComparison.OrdinalIgnoreCase);
        _categories = [.. revitRepository.GetCategories()
            .Where(item => item != null)
            .Select(item => new CategoryEntry(item, addBuiltInNames))];
    }

    /// <summary>
    /// Переводит поисковые наборы файла. Явные наборы (выбор элементов) и наборы, у которых
    /// не нашлось ни одной категории, пропускаются с записью в отчёт.
    /// </summary>
    public List<Filter> ImportSearchSets(NavisExchange exchange, NavisworksImportReport report) {
        List<Filter> filters = [];
        foreach(var set in exchange.SearchSets) {
            if(set.IsExplicitSelection) {
                report.Skip(set.Path, Text("Import.ExplicitSet"));
                continue;
            }

            var filter = ConvertSet(set, exchange.Units, report);
            if(filter is not null) {
                filters.Add(filter);
            }
        }
        return filters;
    }

    /// <summary>
    /// Создаёт проверки из тестов Clash Detective по уже существующим поисковым наборам.
    /// </summary>
    /// <param name="filters">Наборы, на которые могут ссылаться тесты, - по имени.</param>
    public List<Check> ImportClashTests(
        NavisExchange exchange,
        IReadOnlyCollection<Filter> filters,
        NavisworksImportReport report) {

        var documentNames = _revitRepository.DocInfos.Select(item => item.Name).ToList();
        List<Check> checks = [];
        foreach(var test in exchange.ClashTests) {
            if(test.UnsupportedLocator is not null) {
                report.Skip(test.Name, Text("Import.TestNotSearchSet", test.UnsupportedLocator));
                continue;
            }

            // Тест без одной из сторон - проверка набора самого с собой.
            string leftPath = test.Left ?? test.Right;
            string rightPath = test.Right ?? test.Left;
            if(leftPath is null) {
                report.Skip(test.Name, Text("Import.TestEmpty"));
                continue;
            }

            var left = FindFilter(filters, leftPath);
            var right = FindFilter(filters, rightPath);
            if(left is null || right is null) {
                report.Skip(test.Name, Text("Import.TestSetMissing", left is null ? leftPath : rightPath));
                continue;
            }

            var leftFiles = MatchFiles(left, documentNames);
            var rightFiles = MatchFiles(right, documentNames);
            if(leftFiles.Count == 0 || rightFiles.Count == 0) {
                var missing = leftFiles.Count == 0 ? left : right;
                report.Skip(test.Name, Text("Import.TestNoFiles", string.Join(", ", missing.SourceFilePatterns)));
                continue;
            }

            checks.Add(new Check {
                Name = test.Name,
                FirstSelection = new SelectionConfig { Filters = [left.Name], Files = leftFiles },
                SecondSelection = new SelectionConfig { Filters = [right.Name], Files = rightFiles }
            });
        }
        return checks;
    }

    private Filter ConvertSet(NavisSearchSet set, string units, NavisworksImportReport report) {
        List<GroupResult> groups = [];
        foreach(var conditions in set.Groups) {
            var group = ConvertGroup(set, conditions, units, report);
            if(group is not null) {
                groups.Add(group);
            }
        }

        if(groups.Count == 0) {
            report.Skip(set.Path, Text("Import.SetNoCategories"));
            return null;
        }

        var categoryIds = groups.SelectMany(item => item.Categories).Distinct().ToList();
        var patterns = groups.SelectMany(item => item.FilePatterns)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new Filter(_revitRepository) {
            Name = set.Name,
            CategoryIds = categoryIds,
            Set = BuildSet(set, groups, report),
            SourceFilePatterns = patterns.Count == 0 ? null : patterns
        };
    }

    /// <summary>
    /// Собирает дерево правил из ИЛИ-групп.
    /// </summary>
    /// <remarks>
    /// Категории у набора плагина общие на весь набор, поэтому группы сливаются: категории -
    /// объединением, правила - через "ИЛИ" по группам. Если правила у всех групп одинаковы
    /// (типично: та же группа на русском и английском), ИЛИ не нужно. Группа без правил
    /// означает "все элементы её категорий" - тогда правила остальных групп не сузят набор,
    /// и он остаётся без правил, с пометкой в отчёте.
    /// </remarks>
    private Set BuildSet(NavisSearchSet set, IReadOnlyList<GroupResult> groups, NavisworksImportReport report) {
        var and = SetEvaluatorUtils.GetEvaluators().First(item => item.Evaluator == SetEvaluators.And);
        var or = SetEvaluatorUtils.GetEvaluators().First(item => item.Evaluator == SetEvaluators.Or);

        var signatures = groups.Select(item => item.Signature).Distinct().ToList();
        if(signatures.Count == 1) {
            return new Set { SetEvaluator = and, Criteria = [.. groups[0].Rules] };
        }

        if(groups.Any(item => item.Rules.Count == 0)) {
            report.Note(set.Path, Text("Import.SetRulesDropped"));
            return new Set { SetEvaluator = and };
        }

        return new Set {
            SetEvaluator = or,
            Criteria = [.. groups.Select(item => new Set { SetEvaluator = and, Criteria = [.. item.Rules] })]
        };
    }

    private GroupResult ConvertGroup(
        NavisSearchSet set,
        IReadOnlyList<NavisCondition> conditions,
        string units,
        NavisworksImportReport report) {

        HashSet<ElementId> categories = null;
        List<string> patterns = [];
        List<NavisCondition> parameterConditions = [];

        foreach(var condition in conditions) {
            switch(Classify(condition)) {
                case ConditionKind.SourceFile:
                    if(ToFilePattern(condition) is { } pattern) {
                        patterns.Add(pattern);
                    } else {
                        report.Skip(set.Path, Text("Import.ConditionUnsupportedTest", condition));
                    }
                    break;
                case ConditionKind.Category:
                case ConditionKind.ItemName:
                    var matched = MatchCategories(condition);
                    if(matched is null) {
                        report.Skip(set.Path, Text("Import.ConditionUnsupportedTest", condition));
                        break;
                    }
                    categories = categories is null ? matched : [.. categories.Intersect(matched)];
                    break;
                case ConditionKind.Parameter:
                    parameterConditions.Add(condition);
                    break;
                default:
                    report.Skip(set.Path, Text("Import.ConditionUnknownProperty", condition));
                    break;
            }
        }

        // Группа без условия на категорию в Navisworks ищет по всей модели. Плагину нужен список
        // категорий - берутся все модельные, а правила по параметрам сузят выборку.
        categories ??= [.. _categories.Select(item => item.Category.Id)];
        if(categories.Count == 0) {
            // Типично для группы на другом языке: "Стен" в английском Ревите не найдёт категорий.
            return null;
        }

        List<Rule> rules = [];
        foreach(var condition in parameterConditions) {
            if(ConvertRule(condition, categories, units, out string reason) is { } rule) {
                rules.Add(rule);
            } else {
                report.Skip(set.Path, Text("Import.ConditionSkipped", condition, reason));
            }
        }

        return new GroupResult(categories, rules, patterns);
    }

    private ConditionKind Classify(NavisCondition condition) {
        switch(condition.PropertyInternal) {
            case "LcOaNodeSourceFile":
                return ConditionKind.SourceFile;
            case "LcOaSceneBaseClassUserName":
            case "LcRevitPropertyElementCategory":
                return ConditionKind.Category;
            case "LcOaSceneBaseUserName":
                return ConditionKind.ItemName;
        }

        return condition.CategoryInternal.StartsWith("LcRevitData", StringComparison.Ordinal)
            ? ConditionKind.Parameter
            : ConditionKind.Unknown;
    }

    /// <summary>
    /// Категории, подходящие под условие; <see langword="null" />, если проверку не перевести.
    /// </summary>
    private HashSet<ElementId> MatchCategories(NavisCondition condition) {
        var matcher = CreateTextMatcher(condition.Test, condition.Value);
        if(matcher is null) {
            return null;
        }
        return [.. _categories.Where(item => item.Names.Any(matcher)).Select(item => item.Category.Id)];
    }

    /// <summary>
    /// Проверка имени по условию Navisworks, без учёта регистра, пробелов и подчёркиваний.
    /// </summary>
    private static Func<string, bool> CreateTextMatcher(string test, string value) {
        string expected = Normalize(value);
        switch(test) {
            case "equals":
                return name => name == expected;
            case "not_equals":
                return name => name != expected;
            case "contains":
                return name => name.Contains(expected);
            case "wildcard":
                var regex = new Regex(
                    "^" + Regex.Escape(expected).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
                    RegexOptions.CultureInvariant);
                return regex.IsMatch;
            default:
                return null;
        }
    }

    private static string ToFilePattern(NavisCondition condition) {
        string value = condition.Value?.Trim();
        if(string.IsNullOrEmpty(value)) {
            return null;
        }
        return condition.Test switch {
            "wildcard" => value,
            "equals" => value,
            "contains" => "*" + value + "*",
            _ => null
        };
    }

    /// <summary>
    /// Файлы проверки для набора: открытый файл и связи, имена которых подходят под шаблоны
    /// "Исходного файла"; без шаблонов - все. Имя сравнивается и с расширением .rvt / .nwc:
    /// в Navisworks исходный файл - это имя выгрузки, а не документа Ревита.
    /// </summary>
    private static List<string> MatchFiles(Filter filter, IReadOnlyCollection<string> documentNames) {
        if(filter.SourceFilePatterns is not { Count: > 0 } patterns) {
            return [.. documentNames];
        }

        var regexes = patterns
            .Select(item => new Regex(
                "^" + Regex.Escape(item).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .ToList();
        return [.. documentNames.Where(name => regexes.Any(regex =>
            regex.IsMatch(name) || regex.IsMatch(name + ".rvt") || regex.IsMatch(name + ".nwc")))];
    }

    private static Filter FindFilter(IEnumerable<Filter> filters, string path) {
        string name = path.Split('/').Last();
        return filters.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.CurrentCultureIgnoreCase));
    }

    private Rule ConvertRule(NavisCondition condition, ICollection<ElementId> categories, string units, out string reason) {
        var revitParam = ResolveParameter(condition, categories);
        if(revitParam is null) {
            reason = Text("Import.ReasonNoParameter", condition.PropertyName);
            return null;
        }

        var evaluatorKind = GetEvaluator(condition, revitParam.StorageType, out string value);
        var evaluator = evaluatorKind is null
            ? null
            : RuleEvaluatorUtils.GetRuleEvaluators(revitParam.StorageType)
                .FirstOrDefault(item => item.Evaluator == evaluatorKind);
        if(evaluator is null) {
            reason = Text("Import.ReasonNoEvaluator", condition.Test);
            return null;
        }

        ParamValue paramValue = null;
        if(evaluator.Evaluator is not RuleEvaluators.FilterHasValue and not RuleEvaluators.FilterHasNoValue) {
            paramValue = CreateValue(revitParam, condition, value, units);
            if(paramValue is null) {
                reason = Text("Import.ReasonBadValue", condition.Value);
                return null;
            }
        }

        reason = null;
        return new Rule {
            Provider = new ParameterValueProvider(_revitRepository, revitParam),
            Evaluator = evaluator,
            Value = paramValue
        };
    }

    private RevitParam ResolveParameter(NavisCondition condition, ICollection<ElementId> categories) {
        switch(condition.PropertyInternal) {
            case "LcRevitPropertyElementFamily":
                return CreateBuiltIn(BuiltInParameter.ALL_MODEL_FAMILY_NAME);
            case "LcRevitPropertyElementName":
            case "LcRevitPropertyElementType":
                return CreateBuiltIn(BuiltInParameter.ALL_MODEL_TYPE_NAME);
        }

        // Встроенный параметр Navisworks пишет по id: lcldrevit_parameter_-1140238. Enum.ToObject,
        // а не приведение int: в Ревите 2024+ у BuiltInParameter базовый тип long, и IsDefined
        // с int бросает исключение.
        if(condition.PropertyInternal.StartsWith(_builtInParameterPrefix, StringComparison.Ordinal)
           && long.TryParse(condition.PropertyInternal.Substring(_builtInParameterPrefix.Length),
               NumberStyles.Integer, CultureInfo.InvariantCulture, out long id)) {
            var parameter = (BuiltInParameter) Enum.ToObject(typeof(BuiltInParameter), id);
            // Id из выгрузки Navisworks не всегда совпадает со смыслом в этой версии Ревита:
            // "Толщина" перекрытия приходит как -1007322, а это толщина импоста. Такой параметр
            // по категориям набора не фильтруется - тогда ищем по имени.
            if(Enum.IsDefined(typeof(BuiltInParameter), parameter)
               && GetFilterable(categories).Contains(new ElementId(parameter))
               && CreateBuiltIn(parameter) is { } byId) {
                return byId;
            }
        }

        return ResolveByName(condition.PropertyName, categories);
    }

    /// <summary>
    /// Параметр по видимому имени: сначала параметр проекта или общий, затем встроенный с такой
    /// подписью - из тех, по которым можно фильтровать выбранные категории.
    /// </summary>
    private RevitParam ResolveByName(string name, ICollection<ElementId> categories) {
        if(string.IsNullOrWhiteSpace(name)) {
            return null;
        }

        var parameterElement = new FilteredElementCollector(_doc)
            .OfClass(typeof(ParameterElement))
            .Cast<ParameterElement>()
            .FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.CurrentCultureIgnoreCase));
        if(parameterElement is not null) {
            try {
                return ParameterInitializer.InitializeParameter(_doc, parameterElement.Id);
            } catch(ArgumentException) {
                // Не параметр проекта и не общий - ищем дальше среди встроенных.
            }
        }

        if(!GetBuiltInParametersByLabel().TryGetValue(name.Trim(), out var candidates)) {
            return null;
        }

        var filterable = GetFilterable(categories);
        return candidates
            .Where(item => filterable.Contains(new ElementId(item)))
            .Select(CreateBuiltIn)
            .FirstOrDefault(item => item is not null);
    }

    /// <summary>
    /// Параметры, по которым фильтруется хотя бы одна из категорий.
    /// </summary>
    /// <remarks>
    /// Не общие для всех: набор "трубы диаметром больше 50" охватывает и фитинги, и изоляцию,
    /// у которых наружного диаметра нет. Navisworks такие элементы просто не берёт, и фильтр
    /// Ревита по параметру ведёт себя так же.
    /// </remarks>
    private HashSet<ElementId> GetFilterable(ICollection<ElementId> categories) {
        HashSet<ElementId> filterable = [];
        foreach(var category in categories) {
            string key = category.ToString();
            if(!_filterableByCategory.TryGetValue(key, out var parameters)) {
                parameters = [.. ParameterFilterUtilities.GetFilterableParametersInCommon(_doc, [category])];
                _filterableByCategory[key] = parameters;
            }
            filterable.UnionWith(parameters);
        }
        return filterable;
    }

    private Dictionary<string, List<BuiltInParameter>> GetBuiltInParametersByLabel() {
        if(_builtInParametersByLabel is not null) {
            return _builtInParametersByLabel;
        }

        _builtInParametersByLabel = new Dictionary<string, List<BuiltInParameter>>(StringComparer.CurrentCultureIgnoreCase);
        foreach(BuiltInParameter parameter in Enum.GetValues(typeof(BuiltInParameter))) {
            string label;
            try {
                label = LabelUtils.GetLabelFor(parameter);
            } catch(Autodesk.Revit.Exceptions.ApplicationException) {
                continue;
            }
            if(string.IsNullOrWhiteSpace(label)) {
                continue;
            }
            if(!_builtInParametersByLabel.TryGetValue(label, out var list)) {
                _builtInParametersByLabel[label] = list = [];
            }
            list.Add(parameter);
        }
        return _builtInParametersByLabel;
    }

    /// <summary>
    /// Встроенный параметр; <see langword="null" />, если плагину с ним не работать.
    /// </summary>
    /// <remarks>
    /// У числового параметра единицы должны определяться: окно наборов разбирает текст правила
    /// по ним, и параметр без них (dosymep не знает, например, толщину импоста) ронял бы окно
    /// при правке.
    /// </remarks>
    private RevitParam CreateBuiltIn(BuiltInParameter parameter) {
        try {
            var revitParam = SystemParamsConfig.Instance.CreateRevitParam(_doc, parameter);
            if(revitParam.StorageType == StorageType.Double) {
                _ = revitParam.UnitType;
            }
            return revitParam;
        } catch(Exception) {
            return null;
        }
    }

    /// <summary>
    /// Проверка Navisworks в терминах плагина. Шаблон <c>wildcard</c> переводится, только если
    /// он сводится к "начинается", "заканчивается", "содержит" или "равно".
    /// </summary>
    private static RuleEvaluators? GetEvaluator(NavisCondition condition, StorageType storageType, out string value) {
        value = condition.Value;
        bool numeric = storageType is StorageType.Double or StorageType.Integer;
        switch(condition.Test) {
            case "equals":
                return numeric ? RuleEvaluators.FilterNumericEquals : RuleEvaluators.FilterStringEquals;
            case "not_equals":
                return RuleEvaluators.FilterNotEquals;
            case "contains":
                return numeric ? null : RuleEvaluators.FilterStringContains;
            case "greater_than":
                return numeric ? RuleEvaluators.FilterNumericGreater : RuleEvaluators.FilterStringGreater;
            case "greater_equal":
                return numeric ? RuleEvaluators.FilterNumericGreaterOrEqual : RuleEvaluators.FilterStringGreaterOrEqual;
            case "less_than":
                return numeric ? RuleEvaluators.FilterNumericLess : RuleEvaluators.FilterStringLess;
            case "less_equal":
                return numeric ? RuleEvaluators.FilterNumericLessOrEqual : RuleEvaluators.FilterStringLessOrEqual;
            case "defined":
                return RuleEvaluators.FilterHasValue;
            case "undefined":
                return RuleEvaluators.FilterHasNoValue;
            case "wildcard" when !numeric && value is not null:
                return FromWildcard(value, out value);
            default:
                return null;
        }
    }

    private static RuleEvaluators? FromWildcard(string pattern, out string value) {
        bool starts = pattern.StartsWith("*", StringComparison.Ordinal);
        bool ends = pattern.EndsWith("*", StringComparison.Ordinal) && pattern.Length > 1;
        value = pattern.Trim('*');
        if(value.Length == 0 || value.IndexOfAny(['*', '?']) >= 0) {
            return null;
        }
        return (starts, ends) switch {
            (true, true) => RuleEvaluators.FilterStringContains,
            (false, true) => RuleEvaluators.FilterStringBeginsWith,
            (true, false) => RuleEvaluators.FilterStringEndsWith,
            _ => RuleEvaluators.FilterStringEquals
        };
    }

    /// <summary>
    /// Значение правила. Числа Navisworks пишет в единицах файла (<c>units</c>), Ревит хранит
    /// во внутренних - футах; подпись делается в метрических единицах, теми же правилами, что
    /// разбирает текст правила окно наборов.
    /// </summary>
    private static ParamValue CreateValue(RevitParam revitParam, NavisCondition condition, string value, string units) {
        if(value is null) {
            return null;
        }

        switch(revitParam.StorageType) {
            case StorageType.String:
                return new StringParamValue(value, value);
            case StorageType.ElementId:
                // Значение типа "name" у Navisworks выглядит как: Level "FIRST FLOOR", #57775.
                string name = ExtractQuoted(value) ?? value;
                return new ElementIdParamValue(name, name);
            case StorageType.Integer:
                if(bool.TryParse(value, out bool flag)) {
                    return new IntParamValue(flag ? 1 : 0);
                }
                return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)
                    ? new IntParamValue(number)
                    : null;
            case StorageType.Double:
                if(!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double raw)) {
                    return null;
                }
                double internalValue = ToInternal(raw, condition.DataType, units);
                return new DoubleParamValue(internalValue, FormatDisplay(revitParam, internalValue));
            default:
                return null;
        }
    }

    private static double ToInternal(double value, string dataType, string units) {
        double feet = units switch {
            "m" => 1 / 0.3048,
            "cm" => 1 / 30.48,
            "mm" => 1 / 304.8,
            "km" => 1000 / 0.3048,
            "in" => 1 / 12d,
            "yd" => 3,
            "mi" => 5280,
            _ => 1
        };
        return dataType switch {
            "linear" => value * feet,
            "area" => value * feet * feet,
            "volume" => value * feet * feet * feet,
            _ => value
        };
    }

    private static string FormatDisplay(RevitParam revitParam, double internalValue) {
        try {
            if(!string.IsNullOrEmpty(revitParam.UnitType?.TypeId)
               && UnitUtils.IsMeasurableSpec(revitParam.UnitType)) {
                return UnitFormatUtils.Format(new Units(UnitSystem.Metric), revitParam.UnitType, internalValue, true);
            }
        } catch(Exception) {
            // Подпись - удобство: если единицы не форматируются, остаётся число как есть.
        }
        return internalValue.ToString(CultureInfo.CurrentCulture);
    }

    private static string ExtractQuoted(string value) {
        int start = value.IndexOf('"');
        int end = start < 0 ? -1 : value.IndexOf('"', start + 1);
        return end > start ? value.Substring(start + 1, end - start - 1) : null;
    }

    private static string Normalize(string value) {
        return new string((value ?? string.Empty)
            .Where(ch => !char.IsWhiteSpace(ch) && ch != '_' && ch != '-')
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private string Text(string key, params object[] args) {
        return _localization.GetLocalizedString(key, args);
    }

    private enum ConditionKind {
        Unknown,
        SourceFile,
        Category,
        ItemName,
        Parameter
    }

    /// <summary>
    /// Категория модели и имена, по которым её узнаёт условие Navisworks: имя в языке Ревита
    /// и имя <see cref="BuiltInCategory" /> без <c>OST_</c> - английское.
    /// </summary>
    private sealed class CategoryEntry {
        public CategoryEntry(Category category, bool addBuiltInName) {
            Category = category;
            List<string> names = [Normalize(category.Name)];
            var builtInCategory = Enum.ToObject(typeof(BuiltInCategory), category.Id.GetIdValue());
            string builtIn = addBuiltInName && Enum.IsDefined(typeof(BuiltInCategory), builtInCategory)
                ? builtInCategory.ToString()
                : null;
            if(builtIn is not null) {
                names.Add(Normalize(builtIn.StartsWith("OST_", StringComparison.Ordinal) ? builtIn.Substring(4) : builtIn));
            }
            Names = names;
        }

        public Category Category { get; }
        public IReadOnlyList<string> Names { get; }
    }

    private sealed class GroupResult {
        public GroupResult(ICollection<ElementId> categories, IReadOnlyList<Rule> rules, IReadOnlyList<string> filePatterns) {
            Categories = categories;
            Rules = rules;
            FilePatterns = filePatterns;
            Signature = string.Join("|", rules.Select(rule =>
                $"{rule.Provider.Name}:{rule.Evaluator.Evaluator}:{rule.Value?.DisplayValue}"));
        }

        public ICollection<ElementId> Categories { get; }
        public IReadOnlyList<Rule> Rules { get; }
        public IReadOnlyList<string> FilePatterns { get; }
        public string Signature { get; }
    }
}

/// <summary>
/// Что импорт не смог перевести и что перевёл приблизительно.
/// </summary>
internal sealed class NavisworksImportReport {
    private readonly List<string> _skipped = [];
    private readonly List<string> _notes = [];

    public IReadOnlyList<string> Skipped => _skipped;
    public IReadOnlyList<string> Notes => _notes;

    public void Skip(string source, string reason) {
        _skipped.Add($"{source}: {reason}");
    }

    public void Note(string source, string text) {
        _notes.Add($"{source}: {text}");
    }
}
