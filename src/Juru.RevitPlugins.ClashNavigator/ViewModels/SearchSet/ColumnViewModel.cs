using Autodesk.Revit.DB;

using dosymep.WPF.ViewModels;

namespace Juru.RevitPlugins.ClashNavigator.ViewModels.SearchSet;
internal class ColumnViewModel : BaseViewModel {
    public string FieldName { get; set; }
    public string Header { get; set; }
    public StorageType FieldType { get; set; }
}
