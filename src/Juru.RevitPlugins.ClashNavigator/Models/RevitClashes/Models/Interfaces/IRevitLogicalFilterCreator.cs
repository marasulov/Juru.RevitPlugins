using System.Collections.Generic;

using Autodesk.Revit.DB;

namespace Juru.RevitPlugins.ClashNavigator.Models.Interfaces;
internal interface IRevitLogicalFilterCreator {
    ElementLogicalFilter Create(IList<ElementFilter> elementFilters);
}
