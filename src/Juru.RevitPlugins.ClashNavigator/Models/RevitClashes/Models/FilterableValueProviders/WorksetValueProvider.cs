using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

using dosymep.Bim4Everyone.SystemParams;
using dosymep.Revit;

using Newtonsoft.Json;

using Juru.RevitPlugins.ClashNavigator.Models.Evaluators;
using Juru.RevitPlugins.ClashNavigator.Models.Interfaces;
using Juru.RevitPlugins.ClashNavigator.Models.Value;

namespace Juru.RevitPlugins.ClashNavigator.Models.FilterableValueProviders;
/// <summary>
/// Псевдопараметр "Рабочий набор" для правил поискового набора.
/// </summary>
/// <remarks>
/// <see cref="RevitRepository.GetParameters" /> рабочий набор из общего списка параметров
/// убирает: хранится он числом (идентификатором набора), и правило по числу пользователю
/// бесполезно. Здесь он сравнивается строкой - по имени рабочего набора, как его видно в
/// свойствах элемента.
/// В файл пишется только тип: имени и прочих свойств у псевдопараметра нет, они одинаковы
/// у всех его экземпляров.
/// </remarks>
internal class WorksetValueProvider : IFilterableValueProvider, IEquatable<WorksetValueProvider> {
    [JsonConstructor]
    public WorksetValueProvider() {
    }

    public WorksetValueProvider(RevitRepository revitRepository) {
        RevitRepository = revitRepository;
    }

    /// <summary>
    /// Подпись параметра на языке Ревита.
    /// </summary>
    [JsonIgnore]
    public string Name => LabelUtils.GetLabelFor(BuiltInParameter.ELEM_PARTITION_PARAM);

    [JsonIgnore]
    public string DisplayValue => Name;

    [JsonIgnore]
    public RevitRepository RevitRepository { get; set; }

#if REVIT_2020_OR_LESS
    [JsonIgnore]
    public UnitType UnitType => UnitType.UT_Number;
#else
    [JsonIgnore]
    public ForgeTypeId UnitType => new();
#endif

    [JsonIgnore]
    public StorageType StorageType => StorageType.String;

    public ParamValue GetElementParamValue(Element item) {
        if(item is null || !item.IsExistsParam(BuiltInParameter.ELEM_PARTITION_PARAM)) {
            return null;
        }
        string value = item.GetParam(BuiltInParameter.ELEM_PARTITION_PARAM).AsValueString();
        return string.IsNullOrEmpty(value) ? null : new StringParamValue(value, value);
    }

    public ParamValue GetParamValueFormString(string value) {
        return new StringParamValue(value, value);
    }

    public FilterRule GetRule(Document doc, IVisiter visiter, ParamValue paramValue) {
        var revitParam = SystemParamsConfig.Instance.CreateRevitParam(doc, BuiltInParameter.ELEM_PARTITION_PARAM);
        return paramValue.GetFilterRule(visiter, doc, revitParam);
    }

    public IEnumerable<RuleEvaluator> GetRuleEvaluators() {
        return RuleEvaluatorUtils.GetRuleEvaluators(StorageType);
    }

    public IEnumerable<ParamValue> GetValues(Category[] categories, RuleEvaluator ruleEvaluator) {
        if(ruleEvaluator.Evaluator is RuleEvaluators.FilterHasNoValue or RuleEvaluators.FilterHasValue) {
            return [];
        }
        var categoryFilter = new ElementMulticategoryFilter(categories.Select(item => item.Id).ToList());
        return RevitRepository.GetCollectors()
            .SelectMany(item => item.RevitCollector
                .WhereElementIsNotElementType()
                .WherePasses(categoryFilter)
                .Select(GetElementParamValue)
                .Where(value => value != null))
            .Distinct()
            .OrderBy(item => item);
    }

    public bool Equals(WorksetValueProvider other) {
        return other != null;
    }

    public override bool Equals(object obj) {
        return Equals(obj as WorksetValueProvider);
    }

    public override int GetHashCode() {
        return typeof(WorksetValueProvider).GetHashCode();
    }
}
