using Autodesk.Revit.DB;

namespace Juru.RevitPlugins.ClashNavigator.ViewModels.FilterCreatorViewModels.Interfaces;
internal interface IParameterElementViewModel {
    string Name { get; }
    StorageType StorageType { get; }
}
