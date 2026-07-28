using System.Collections.Generic;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.ClashNavigator.Models.Interfaces;

namespace Juru.RevitPlugins.ClashNavigator.Models.FilterCreators.RevitFilterCreators;
internal class RevitLogicalOrFilterCreator : IRevitLogicalFilterCreator {
    public ElementLogicalFilter Create(IList<ElementFilter> elementFilters) {
        return new LogicalOrFilter(elementFilters);
    }
}
