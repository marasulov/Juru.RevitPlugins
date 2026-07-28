using System;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.ClashNavigator.Models.Interfaces;

namespace Juru.RevitPlugins.ClashNavigator.Models.Visiter;
internal class NotContainsVisister : IVisiter {
    public FilterRule Create(ElementId paramId, string value) {
#if REVIT_2022_OR_LESS
        return ParameterFilterRuleFactory.CreateNotContainsRule(paramId, value, false);
#else
        return ParameterFilterRuleFactory.CreateNotContainsRule(paramId, value);
#endif
    }

    public FilterRule Create(ElementId paramId, int value) {
        throw new NotImplementedException();
    }

    public FilterRule Create(ElementId paramId, double value) {
        throw new NotImplementedException();
    }

    public FilterRule Create(ElementId paramId, ElementId value) {
        throw new NotImplementedException();
    }
}
