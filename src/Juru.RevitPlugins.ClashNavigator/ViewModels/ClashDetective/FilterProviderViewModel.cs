using Autodesk.Revit.DB;

using dosymep.WPF.ViewModels;

using Juru.RevitPlugins.ClashNavigator.Models.ClashDetection;
using Juru.RevitPlugins.ClashNavigator.Models.FilterModel;
using Juru.RevitPlugins.ClashNavigator.Models.Interfaces;
using Juru.RevitPlugins.ClashNavigator.ViewModels.ClashDetective.Interfaces;

namespace Juru.RevitPlugins.ClashNavigator.ViewModels.ClashDetective;
internal class FilterProviderViewModel : BaseViewModel, IProviderViewModel {
    private readonly Filter _filter;

    public FilterProviderViewModel() { }

    public FilterProviderViewModel(Filter filter) {
        _filter = filter;
        Name = filter.Name;
    }

    public string Name { get; }

    public IProvider GetProvider(Document doc, Transform transform) {
        return new FilterProvider(doc, _filter, transform);
    }
}
