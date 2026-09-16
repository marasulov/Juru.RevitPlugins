using System.Windows.Interop;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Juru.RevitPlugins.Core.Localization;
using Juru.RevitPlugins.Core.Theming;
using Juru.RevitPlugins.Openings.Models.Configs;
using Juru.RevitPlugins.Openings.ViewModels.Settings;
using Juru.RevitPlugins.Openings.Views.Settings;

namespace Juru.RevitPlugins.Openings.Commands;

/// <summary>
/// Открывает настройки выдачи заданий на отверстия.
/// </summary>
// Manual, а не ReadOnly: AddinManager требует одинаковый режим у всех команд сборки.
[Transaction(TransactionMode.Manual)]
public class SetOpeningsSettingsCommand : IExternalCommand {
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) {
        JuruTheme.Apply();

        var config = OpeningsConfig.GetOpeningsConfig();
        var culture = PluginCulture.Resolve(config, commandData.Application.Application.Language);
        var localization = PluginLocalization.Load(typeof(SetOpeningsSettingsCommand), culture);

        var window = new OpeningsSettingsWindow {
            DataContext = new OpeningsSettingsViewModel(config, localization)
        };
        localization.ApplyTo(window);
        ColumnLocalization.Apply(window.OffsetsGrid, localization);
        new WindowInteropHelper(window) { Owner = commandData.Application.MainWindowHandle };

        return window.ShowDialog() == true ? Result.Succeeded : Result.Cancelled;
    }
}
