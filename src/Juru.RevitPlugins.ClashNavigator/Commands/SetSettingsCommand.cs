using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using dosymep.Bim4Everyone;
using dosymep.WpfCore.Ninject;
using dosymep.WpfUI.Core.Ninject;
using Juru.RevitPlugins.Core.Localization;
using Juru.RevitPlugins.ClashNavigator.Models;
using Juru.RevitPlugins.ClashNavigator.Services;
using Juru.RevitPlugins.ClashNavigator.Models.GraphicView;
using Juru.RevitPlugins.ClashNavigator.Models.Handlers;
using Juru.RevitPlugins.ClashNavigator.ViewModels.Settings;
using Juru.RevitPlugins.ClashNavigator.Views.Settings;
using Ninject;
using System.Globalization;

namespace Juru.RevitPlugins.ClashNavigator.Commands;

[Transaction(TransactionMode.Manual)]
public class SetSettingsCommand : BasePluginCommand {
    public SetSettingsCommand() {
        PluginName = "Настройки";
    }

    protected override void Execute(UIApplication uiApplication) {
        // Plain kernel, not uiApplication.CreatePlatformServices() - see comment in GetClashesCommand.
        var kernel = new StandardKernel();

        // CreatePlatformServices() used to supply this; a bare kernel has to bind it itself.
        kernel.Bind<UIApplication>()
            .ToConstant(uiApplication);
        kernel.Bind<RevitRepository>()
            .ToSelf()
            .InSingletonScope();
        kernel.Bind<RevitEventHandler>()
            .ToSelf()
            .InSingletonScope();
        kernel.Bind<ParameterFilterProvider>()
            .ToSelf()
            .InSingletonScope();

        // Constructed directly, not bound as IConfigSerializer - see comment in GetClashesCommand.
        kernel.Bind<SettingsConfig>()
            .ToMethod(c => SettingsConfig.GetSettingsConfig(new dosymep.Serializers.ConfigSerializer()));

        kernel.UsePluginLanguage();

        // Локализация привязывается здесь, до привязки окна и служб диалогов. Окно имеет
        // и полный конструктор, и пустой; Ninject берёт полный только когда все его службы
        // уже привязаны, иначе молча берёт пустой - а тот не подключает
        // WpfLocalizationBehavior, и культура окну не задаётся: надписи из словаря
        // пропадают целиком.
        kernel.UseWpfLocalization(
            PluginResourceUri.Get(GetType(), "assets/Localization/Language.xaml"),
            CultureInfo.GetCultureInfo("ru-RU"));
        kernel.UseWpfUIThemeUpdater();

        kernel.Bind<SettingsViewModel>()
            .ToSelf()
            .InSingletonScope();
        kernel.Bind<SettingsWindow>()
            .ToSelf()
            .InSingletonScope()
            .WithPropertyValue(nameof(System.Windows.Window.DataContext), c => c.Kernel.Get<SettingsViewModel>());


        Notification(kernel.Get<SettingsWindow>());
    }
}
