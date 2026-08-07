using System.Globalization;
using System.Reflection;
using System.Windows;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

using dosymep.Bim4Everyone;
using dosymep.Bim4Everyone.ProjectConfigs;
using dosymep.Bim4Everyone.SimpleServices;
using dosymep.SimpleServices;
using dosymep.WpfCore.Ninject;
using dosymep.WpfUI.Core.Ninject;

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
        // Plain kernel, not uiApplication.CreatePlatformServices() - that builds a ChildKernel off
        // dosymep's static ServicesProvider.Instance/LoadInstance, which pulls in DevExpress via
        // dosymep.Xpf.Core.Ninject and conflicts with the separately-installed real Bim4Everyone
        // plugin's copy of the same dependency. We only ever needed WpfUI-based local services.
        var kernel = new StandardKernel();
        // CreatePlatformServices() used to supply these; a bare kernel has to bind them itself.
        kernel.Bind<UIApplication>()
            .ToConstant(uiApplication);
        kernel.Bind<ILoggerService>()
            .To<dosymep.Serializers.NullLoggerService>()
            .InSingletonScope();
        kernel.Bind<ISerializationService>()
            .To<dosymep.Serializers.JsonSerializationService>()
            .InSingletonScope();
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
        kernel.Bind<IConfigSerializer>()
            .To<dosymep.Serializers.ConfigSerializer>()
            .InSingletonScope();
        kernel.Bind<SettingsConfig>()
            .ToMethod(c => SettingsConfig.GetSettingsConfig(c.Kernel.Get<IConfigSerializer>()));

        // WpfUIMessageBoxService requires these services during construction.
        kernel.UseWpfWindowsTheme();
        kernel.UseWpfWindowsLanguage();
        kernel.UseWpfUIThemeUpdater();

        kernel.UseWpfOpenFileDialog<NavigatorViewModel>(
            filter: "NavisClashReport (*.xml)|*.xml|PluginClashReport (*.json)|*.json");
        kernel.UseWpfSaveFileDialog<NavigatorViewModel>();
        kernel.UseWpfUIMessageBox<NavigatorViewModel>();
        kernel.Bind<NavigatorWindow>()
            .ToSelf()
            .InSingletonScope()
            .WithPropertyValue(nameof(Window.DataContext), c => c.Kernel.Get<NavigatorViewModel>());
        // WpfUIMessageBoxService requires IHasTheme/IHasLocalization to be resolvable — NavigatorWindow provides them.
        kernel.Bind<IHasTheme>()
            .ToMethod(c => c.Kernel.Get<NavigatorWindow>());
        kernel.Bind<IHasLocalization>()
            .ToMethod(c => c.Kernel.Get<NavigatorWindow>());

        string assemblyName = Assembly.GetExecutingAssembly().GetName().Name;
        kernel.UseWpfLocalization(
            $"/{assemblyName};component/assets/Localization/Language.xaml",
            CultureInfo.GetCultureInfo("ru-RU"));

        var window = (Window)kernel.Get<NavigatorWindow>();

       
        window.Show(uiApplication.MainWindowHandle);
    }
}
