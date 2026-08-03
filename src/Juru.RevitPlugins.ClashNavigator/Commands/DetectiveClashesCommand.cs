using System.Globalization;
using System.IO;
using System.Reflection;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

using dosymep.Bim4Everyone;
using dosymep.Bim4Everyone.ProjectConfigs;
using dosymep.Bim4Everyone.SimpleServices;
using dosymep.WpfCore.Ninject;
using dosymep.WpfUI.Core.Ninject;

using Ninject;

using Juru.RevitPlugins.ClashNavigator.Models;
using Juru.RevitPlugins.ClashNavigator.Models.FilterModel;
using Juru.RevitPlugins.ClashNavigator.Models.GraphicView;
using Juru.RevitPlugins.ClashNavigator.Models.Handlers;
using Juru.RevitPlugins.ClashNavigator.ViewModels.ClashDetective;
using Juru.RevitPlugins.ClashNavigator.Views;
using Juru.RevitPlugins.ClashNavigator.Views.Checks;

using Wpf.Ui;

namespace Juru.RevitPlugins.ClashNavigator.Commands;

[Transaction(TransactionMode.Manual)]
public class DetectiveClashesCommand : BasePluginCommand {
    public DetectiveClashesCommand() {
        PluginName = "Поиск коллизий";
    }

    protected override void Execute(UIApplication uiApplication) {
        ExecuteCommand(uiApplication);
    }

    public void ExecuteCommand(UIApplication uiApplication) {
        using var kernel = uiApplication.CreatePlatformServices();
        kernel.Bind<RevitRepository>()
            .ToSelf()
            .InSingletonScope();
        kernel.Bind<RevitEventHandler>()
            .ToSelf()
            .InSingletonScope();
        kernel.Bind<ParameterFilterProvider>()
            .ToSelf()
            .InSingletonScope();
        kernel.Bind<IContentDialogService>()
            .To<ContentDialogService>()
            .InSingletonScope();
        kernel.Bind<FiltersConfig>()
            .ToMethod(c => {
                var repo = c.Kernel.Get<RevitRepository>();
                string path = GetConfigPath(repo);
                return FiltersConfig.GetFiltersConfig(path, repo.Doc);
            });
        kernel.Bind<ChecksConfig>()
            .ToMethod(c => {
                var repo = c.Kernel.Get<RevitRepository>();
                string path = GetConfigPath(repo);
                return ChecksConfig.GetChecksConfig(path, repo.Doc);
            });
        kernel.Bind<SettingsConfig>()
            .ToMethod(c => SettingsConfig.GetSettingsConfig(c.Kernel.Get<IConfigSerializer>()));

        // WpfUIMessageBoxService requires these services during construction.
        kernel.UseWpfWindowsTheme();
        kernel.UseWpfWindowsLanguage();
        kernel.UseWpfUIThemeUpdater();

        kernel.Bind<MainViewModel>()
            .ToSelf()
            .InSingletonScope();
        kernel.Bind<MainWindow>()
            .ToSelf()
            .InSingletonScope()
            .WithPropertyValue(nameof(System.Windows.Window.DataContext), c => c.Kernel.Get<MainViewModel>());
        kernel.UseWpfOpenFileDialog<MainViewModel>()
            .UseWpfSaveFileDialog<MainViewModel>()
            .UseWpfUIMessageBox<MainViewModel>();

        string assemblyName = Assembly.GetExecutingAssembly().GetName().Name;
        kernel.UseWpfLocalization(
            $"/{assemblyName};component/assets/Localization/Language.xaml",
            CultureInfo.GetCultureInfo("ru-RU"));

        Notification(kernel.Get<MainWindow>());
    }

    private string GetConfigPath(RevitRepository revitRepository) {
        return Path.Combine(revitRepository.GetObjectName(), revitRepository.GetDocumentName());
    }
}
