using System.Collections.Generic;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.ClashNavigator.Models.Evaluators;
using Juru.RevitPlugins.ClashNavigator.Models.Value;

namespace Juru.RevitPlugins.ClashNavigator.Models.Interfaces;
internal interface IFilterableValueProvider {
    string Name { get; }
    string DisplayValue { get; }
    RevitRepository RevitRepository { get; set; }
#if REVIT_2020_OR_LESS
    UnitType UnitType { get; }
#else
    ForgeTypeId UnitType { get; }
#endif
    StorageType StorageType { get; }
    ParamValue GetParamValueFormString(string value);
    IEnumerable<RuleEvaluator> GetRuleEvaluators();
    IEnumerable<ParamValue> GetValues(Category[] categories, RuleEvaluator ruleEvaluator);
    ParamValue GetElementParamValue(Element item);
    FilterRule GetRule(Document doc, IVisiter visiter, ParamValue paramValue);
}
