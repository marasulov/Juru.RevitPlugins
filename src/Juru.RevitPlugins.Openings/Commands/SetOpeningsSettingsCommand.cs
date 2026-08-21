using System.Windows.Interop;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Juru.RevitPlugins.Openings.Models.Configs;
using Juru.RevitPlugins.Openings.ViewModels.Settings;
using Juru.RevitPlugins.Openings.Views.Settings;

namespace Juru.RevitPlugins.Openings.Commands;

/// <summary>
/// Открывает настройки выдачи заданий на отверстия.
/// </summary>
[Transaction(TransactionMode.ReadOnly)]
public class SetOpeningsSettingsCommand : IExternalCommand {
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) {
        var config = OpeningsConfig.GetOpeningsConfig();
        var window = new OpeningsSettingsWindow {
            DataContext = new OpeningsSettingsViewModel(config)
        };
        new WindowInteropHelper(window) { Owner = commandData.Application.MainWindowHandle };

        return window.ShowDialog() == true ? Result.Succeeded : Result.Cancelled;
    }
}
