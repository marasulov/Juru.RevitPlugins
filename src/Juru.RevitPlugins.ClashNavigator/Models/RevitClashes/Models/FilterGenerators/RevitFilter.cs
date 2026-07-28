using Autodesk.Revit.DB;

using Juru.RevitPlugins.ClashNavigator.Models.Interfaces;

namespace Juru.RevitPlugins.ClashNavigator.Models.FilterGenerators;
internal class RevitFilter : IFilter {
    public ElementFilter Filter { get; set; }
}
