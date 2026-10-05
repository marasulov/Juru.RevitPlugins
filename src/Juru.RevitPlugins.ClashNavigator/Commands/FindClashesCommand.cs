using System.Globalization;
using System.Windows;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

using dosymep.Bim4Everyone;
using dosymep.Bim4Everyone.SimpleServices;
using dosymep.SimpleServices;
using dosymep.WpfCore.Ninject;
using dosymep.WpfUI.Core.Ninject;

using Juru.RevitPlugins.Core.Localization;
using Juru.RevitPlugins.ClashNavigator.Models;
using Juru.RevitPlugins.ClashNavigator.Models.FilterModel;
using Juru.RevitPlugins.ClashNavigator.Models.GraphicView;
using Juru.RevitPlugins.ClashNavigator.Models.Handlers;
using Juru.RevitPlugins.ClashNavigator.ViewModels.ClashDetective;
using Juru.RevitPlugins.ClashNavigator.Views.Checks;

using Juru.RevitPlugins.ClashNavigator.Services;

using Ninject;

using Wpf.Ui;

namespace Juru.RevitPlugins.ClashNavigator.Commands;
/// <summary>
/// Открывает окно проверок на коллизии: какие выборки с какими сравнивать и чем их набирать.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class FindClashesCommand : BasePluginCommand {
    public FindClashesCommand() {
        PluginName = "Поиск коллизий";
    }

    protected override void Execute(UIApplication uiApplication) {
        // Revit does not create a System.Windows.Application, so Wpf.Ui's own UiApplication.Current
        // falls back to a bare ResourceDictionary that nothing has ever populated. The per-window
        // Wpf.Ui.Appearance.ApplicationThemeManager.Apply(this) calls in SelectableNamesWindow and
        // MessageWindow only COPY resources out of that dictionary - they render nothing new until
        // something has actually populated it. This is that something, once per command.
        Wpf.Ui.Appearance.ApplicationThemeManager.ApplySystemTheme();

        // Plain kernel, not uiApplication.CreatePlatformServices() - see comment in GetClashesCommand.
        var kernel = new StandardKernel();
        kernel.Bind<UIApplication>()
            .ToConstant(uiApplication);
        // ToMethod, not the generic To<T>() - see comment in GetClashesCommand.
        kernel.Bind<ILoggerService>()
            .ToMethod(_ => new dosymep.Serializers.NullLoggerService())
            .InSingletonScope();
        kernel.Bind<ISerializationService>()
            .ToMethod(_ => new dosymep.Serializers.JsonSerializationService())
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
        kernel.Bind<IContentDialogService>()
            .To<ContentDialogService>()
            .InSingletonScope();

        // Constructed directly, not bound as IConfigSerializer - see comment in GetClashesCommand.
        kernel.Bind<SettingsConfig>()
            .ToMethod(c => SettingsConfig.GetSettingsConfig(new dosymep.Serializers.ConfigSerializer()));
        kernel.Bind<ChecksConfig>()
            .ToMethod(c => {
                var repository = c.Kernel.Get<RevitRepository>();
                return ChecksConfig.GetChecksConfig(repository.GetConfigRelativePath(), repository.Doc);
            })
            .InSingletonScope();
        kernel.Bind<FiltersConfig>()
            .ToMethod(c => {
                var repository = c.Kernel.Get<RevitRepository>();
                return FiltersConfig.GetFiltersConfig(repository.GetConfigRelativePath(), repository.Doc);
            })
            .InSingletonScope();

        // WpfUIMessageBoxService requires these services during construction.
        kernel.UseWpfWindowsTheme();
        kernel.UsePluginLanguage();

        // Локализация привязывается здесь, до привязки окна и служб диалогов. Окно имеет
        // и полный конструктор, и пустой; Ninject берёт полный только когда все его службы
        // уже привязаны, иначе молча берёт пустой - а тот не подключает
        // WpfLocalizationBehavior, и культура окну не задаётся: надписи из словаря
        // пропадают целиком. Привязка окна ещё и InSingletonScope, поэтому такой
        // экземпляр запоминается навсегда.
        kernel.UseWpfLocalization(
            PluginResourceUri.Get(GetType(), "assets/Localization/Language.xaml"),
            CultureInfo.GetCultureInfo("ru-RU"));
        kernel.UseWpfUIThemeUpdater();

        kernel.UseWpfOpenFileDialog<MainViewModel>(filter: "ClashConfig (*.json)|*.json");
        kernel.UseWpfSaveFileDialog<MainViewModel>();
        kernel.UseWpfUIMessageBox<MainViewModel>();

        kernel.Bind<MainViewModel>()
            .ToSelf()
            .InSingletonScope();
        kernel.Bind<MainWindow>()
            .ToSelf()
            .InSingletonScope()
            .WithPropertyValue(nameof(Window.DataContext), c => c.Kernel.Get<MainViewModel>());
        // WpfUIMessageBoxService requires IHasTheme/IHasLocalization to be resolvable - MainWindow provides them.
        kernel.Bind<IHasTheme>()
            .ToMethod(c => c.Kernel.Get<MainWindow>());
        kernel.Bind<IHasLocalization>()
            .ToMethod(c => c.Kernel.Get<MainWindow>());


        var window = (Window) kernel.Get<MainWindow>();
        window.Show(uiApplication.MainWindowHandle);
    }
}
