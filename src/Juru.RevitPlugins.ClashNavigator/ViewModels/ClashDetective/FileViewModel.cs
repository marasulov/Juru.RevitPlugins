using Autodesk.Revit.DB;

using dosymep.WPF.ViewModels;

using Juru.RevitPlugins.ClashNavigator.Models;

namespace Juru.RevitPlugins.ClashNavigator.ViewModels.ClashDetective;
internal class FileViewModel : BaseViewModel, IName {
    public FileViewModel() { }

    public FileViewModel(Document doc, Transform transform) {
        Doc = doc ?? throw new System.ArgumentNullException(nameof(doc));
        Transform = transform ?? throw new System.ArgumentNullException(nameof(transform));
        Name = RevitRepository.GetDocumentName(Doc);
    }

    public string Name { get; }

    public Document Doc { get; }

    public Transform Transform { get; }
}
