using System.Collections.Generic;

using Autodesk.Revit.DB;

namespace Juru.RevitPlugins.ClashNavigator.Models.Interfaces;
internal interface ICriterion {
    IFilterGenerator FilterGenerator { get; set; }
    IFilterGenerator Generate(Document doc);
    IEnumerable<IFilterableValueProvider> GetProviders();
}
