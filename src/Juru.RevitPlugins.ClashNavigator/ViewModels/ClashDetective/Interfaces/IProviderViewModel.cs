using Autodesk.Revit.DB;

using Juru.RevitPlugins.ClashNavigator.Models;
using Juru.RevitPlugins.ClashNavigator.Models.Interfaces;

namespace Juru.RevitPlugins.ClashNavigator.ViewModels.ClashDetective.Interfaces;
internal interface IProviderViewModel : IName {
    IProvider GetProvider(Document doc, Transform transform);
}
