using Autodesk.Revit.DB;

using Juru.RevitPlugins.ClashNavigator.Models.Interfaces;

namespace Juru.RevitPlugins.ClashNavigator.Models.FilterGenerators;
internal class RevitRule : IRule {
    public FilterRule FilterRule { get; set; }
}
