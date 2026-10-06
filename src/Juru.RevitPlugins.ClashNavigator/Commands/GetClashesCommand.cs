using System.Windows;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

using dosymep.Bim4Everyone;

using Juru.RevitPlugins.ClashNavigator.Services;

namespace Juru.RevitPlugins.ClashNavigator.Commands;
[Transaction(TransactionMode.Manual)]
public class GetClashesCommand : BasePluginCommand {
    public GetClashesCommand() {
        PluginName = "Навигатор";
    }

    protected override void Execute(UIApplication uiApplication) {
        Window window = NavigatorWindowFactory.Create(uiApplication);
        window.Show(uiApplication.MainWindowHandle);
    }
}
