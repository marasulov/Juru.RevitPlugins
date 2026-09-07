using System.Windows;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

using dosymep.Bim4Everyone;

using Juru.RevitPlugins.ClashNavigator.Services;

namespace Juru.RevitPlugins.ClashNavigator.Commands;
/// <summary>
/// Открывает окно поисковых наборов - тех, которыми проверки набирают элементы для сравнения.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class SetFiltersCommand : BasePluginCommand {
    public SetFiltersCommand() {
        PluginName = "Поисковые наборы";
    }

    protected override void Execute(UIApplication uiApplication) {
        var window = SearchSetsWindowFactory.Create(uiApplication);
        ((Window) window).Show(uiApplication.MainWindowHandle);
    }
}
