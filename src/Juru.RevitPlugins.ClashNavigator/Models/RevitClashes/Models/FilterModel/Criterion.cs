using System.Collections.Generic;

using Autodesk.Revit.DB;

using Newtonsoft.Json;

using Juru.RevitPlugins.ClashNavigator.Models.Interfaces;

namespace Juru.RevitPlugins.ClashNavigator.Models.FilterModel;
internal abstract class Criterion : ICriterion {
    [JsonIgnore]
    public RevitRepository RevitRepository { get; set; }
    [JsonIgnore]
    public IFilterGenerator FilterGenerator { get; set; }
    public virtual void SetRevitRepository(RevitRepository revitRepository) {
        RevitRepository = revitRepository;
    }
    public abstract IFilterGenerator Generate(Document doc);
    public abstract IEnumerable<IFilterableValueProvider> GetProviders();
}
