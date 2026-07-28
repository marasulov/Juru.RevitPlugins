using System.Globalization;
using System.Reflection;
using System.Windows;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

using dosymep.Bim4Everyone;
using dosymep.Bim4Everyone.ProjectConfigs;
using dosymep.Bim4Everyone.SimpleServices;
using dosymep.WpfCore.Ninject;
using dosymep.Xpf.Core.Ninject;

using Ninject;

using Juru.RevitPlugins.ClashNavigator.Models;
using Juru.RevitPlugins.ClashNavigator.Models.GraphicView;
using Juru.RevitPlugins.ClashNavigator.Models.Handlers;
using Juru.RevitPlugins.ClashNavigator.ViewModels.Navigator;
using Juru.RevitPlugins.ClashNavigator.Views;
using Juru.RevitPlugins.ClashNavigator.Views.Navigator;

using Wpf.Ui;

namespace Juru.RevitPlugins.ClashNavigator.Commands;
[Transaction(TransactionMode.Manual)]
public class GetClashesCommand : BasePluginCommand {
    public GetClashesCommand() {
        PluginName = "Навигатор";
    }

    protected override void Execute(UIApplication uiApplication) {
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
        kernel.Bind<NavigatorViewModel>()
            .ToSelf()
            .InSingletonScope()
            .WithConstructorArgument("reportName", (string) null);
        kernel.Bind<IContentDialogService>()
            .To<ContentDialogService>()
            .InSingletonScope();
        kernel.Bind<SettingsConfig>()
            .ToMethod(c => SettingsConfig.GetSettingsConfig(c.Kernel.Get<IConfigSerializer>()));
        kernel.UseWpfOpenFileDialog<NavigatorViewModel>(
            filter: "NavisClashReport (*.xml)|*.xml|PluginClashReport (*.json)|*.json");
        kernel.UseWpfSaveFileDialog<NavigatorViewModel>();
        kernel.UseXtraMessageBox<NavigatorViewModel>();
        kernel.Bind<NavigatorWindow>()
            .ToSelf()
            .InSingletonScope()
            .WithPropertyValue(nameof(Window.DataContext), c => c.Kernel.Get<NavigatorViewModel>());

        string assemblyName = Assembly.GetExecutingAssembly().GetName().Name;
        kernel.UseWpfLocalization(
            $"/{assemblyName};component/assets/Localization/Language.xaml",
            CultureInfo.GetCultureInfo("ru-RU"));

        var window = (Window)kernel.Get<NavigatorWindow>();

       
        window.Show(uiApplication.MainWindowHandle);
    }
}
