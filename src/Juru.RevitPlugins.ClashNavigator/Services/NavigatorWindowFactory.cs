using System.Globalization;
using System.Windows;

using Autodesk.Revit.UI;

using dosymep.Bim4Everyone.SimpleServices;
using dosymep.SimpleServices;
using dosymep.WpfCore.Ninject;
using dosymep.WpfUI.Core.Ninject;

using Juru.RevitPlugins.ClashNavigator.Models;
using Juru.RevitPlugins.ClashNavigator.Models.GraphicView;
using Juru.RevitPlugins.ClashNavigator.Models.Handlers;
using Juru.RevitPlugins.ClashNavigator.ViewModels.Navigator;
using Juru.RevitPlugins.ClashNavigator.Views.Navigator;
using Juru.RevitPlugins.Core.Localization;

using Ninject;

using Wpf.Ui;

namespace Juru.RevitPlugins.ClashNavigator.Services;
/// <summary>
/// Собирает окно навигатора. Его открывают из двух мест: кнопкой на ленте и кнопкой отчёта в
/// строке проверки, - поэтому сборка ядра живёт здесь, а не в команде.
/// </summary>
/// <remarks>
/// Кнопка проверки раньше создавала окно пустым конструктором, в обход ядра: без службы
/// локализации окно не получало словаря, а WPF-UI-окну в таком виде не хватало служб темы.
/// </remarks>
internal static class NavigatorWindowFactory {
    /// <param name="revitRepository">
    /// Готовый репозиторий, если он у вызывающей стороны уже есть. Второй экземпляр создавать
    /// нельзя без нужды: его конструктор в транзакции создаёт 3D-вид для коллизий.
    /// </param>
    /// <param name="reportName">
    /// Отчёт проверки, который открыть сразу; <see langword="null" /> - все отчёты файла.
    /// </param>
    public static NavigatorWindow Create(
        UIApplication uiApplication,
        RevitRepository revitRepository = null,
        string reportName = null) {

        // Plain kernel, not uiApplication.CreatePlatformServices() - that builds a ChildKernel off
        // dosymep's static ServicesProvider.Instance/LoadInstance, which pulls in DevExpress via
        // dosymep.Xpf.Core.Ninject and conflicts with the separately-installed real Bim4Everyone
        // plugin's copy of the same dependency. We only ever needed WpfUI-based local services.
        var kernel = new StandardKernel();
        // CreatePlatformServices() used to supply these; a bare kernel has to bind them itself.
        kernel.Bind<UIApplication>()
            .ToConstant(uiApplication);
        // ToMethod, not the generic To<T>() - dosymep.SimpleServices can be loaded twice under AddinManager,
        // and To<T>()'s runtime constraint check throws a VerificationException when it is.
        kernel.Bind<ILoggerService>()
            .ToMethod(_ => new dosymep.Serializers.NullLoggerService())
            .InSingletonScope();
        kernel.Bind<ISerializationService>()
            .ToMethod(_ => new dosymep.Serializers.JsonSerializationService())
            .InSingletonScope();

        if(revitRepository is null) {
            kernel.Bind<RevitRepository>()
                .ToSelf()
                .InSingletonScope();
        } else {
            kernel.Bind<RevitRepository>()
                .ToConstant(revitRepository);
        }

        kernel.Bind<RevitEventHandler>()
            .ToSelf()
            .InSingletonScope();
        kernel.Bind<ParameterFilterProvider>()
            .ToSelf()
            .InSingletonScope();
        kernel.Bind<NavigatorViewModel>()
            .ToSelf()
            .InSingletonScope()
            .WithConstructorArgument("reportName", reportName)
            .OnActivation(viewModel => viewModel.OpenFromClashDetector = reportName != null);
        kernel.Bind<IContentDialogService>()
            .To<ContentDialogService>()
            .InSingletonScope();
        // Constructed directly rather than bound as IConfigSerializer: Ninject's generic To<T>() verifies
        // the TImplementation : TService constraint at runtime, which throws a VerificationException when
        // AddinManager side-loads this assembly and dosymep.Bim4Everyone ends up loaded twice under two
        // identities. Nothing else resolves IConfigSerializer from the kernel.
        kernel.Bind<SettingsConfig>()
            .ToMethod(c => SettingsConfig.GetSettingsConfig(new dosymep.Serializers.ConfigSerializer()));

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
            PluginResourceUri.Get(typeof(NavigatorWindowFactory), "assets/Localization/Language.xaml"),
            CultureInfo.GetCultureInfo("ru-RU"));
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

        return kernel.Get<NavigatorWindow>();
    }
}
