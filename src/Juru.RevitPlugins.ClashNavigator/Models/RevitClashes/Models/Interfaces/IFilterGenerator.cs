using Autodesk.Revit.DB;

using Juru.RevitPlugins.ClashNavigator.Models.FilterModel;

namespace Juru.RevitPlugins.ClashNavigator.Models.Interfaces;
internal interface IFilterGenerator {
    IFilterGenerator SetSetFilter(Document doc, Set set);
    IFilterGenerator SetRuleFilter(Document doc, Rule rule);
}
