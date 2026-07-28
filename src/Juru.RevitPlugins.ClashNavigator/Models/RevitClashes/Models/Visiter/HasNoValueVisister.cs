
using Autodesk.Revit.DB;

using Juru.RevitPlugins.ClashNavigator.Models.Interfaces;

namespace Juru.RevitPlugins.ClashNavigator.Models.Visiter;
internal class HasNoValueVisister : IVisiter {
    public FilterRule Create(ElementId paramId, int value) {
        return ParameterFilterRuleFactory.CreateHasNoValueParameterRule(paramId);
    }

    public FilterRule Create(ElementId paramId, double value) {
        return ParameterFilterRuleFactory.CreateHasNoValueParameterRule(paramId);
    }

    public FilterRule Create(ElementId paramId, string value) {
        return ParameterFilterRuleFactory.CreateHasNoValueParameterRule(paramId);
    }

    public FilterRule Create(ElementId paramId, ElementId value) {
        return ParameterFilterRuleFactory.CreateHasNoValueParameterRule(paramId);
    }
}
