using System.Globalization;
using System.Reflection;
using System.Windows;

using Autodesk.Revit.UI;

using dosymep.Bim4Everyone.SimpleServices;
using dosymep.SimpleServices;
using dosymep.WpfCore.Ninject;
using dosymep.WpfUI.Core.Ninject;

using Juru.RevitPlugins.ClashNavigator.Models;
using Juru.RevitPlugins.ClashNavigator.Models.FilterModel;
using Juru.RevitPlugins.ClashNavigator.Models.GraphicView;
using Juru.RevitPlugins.ClashNavigator.Models.Handlers;
using Juru.RevitPlugins.ClashNavigator.ViewModels.SearchSet;
using Juru.RevitPlugins.ClashNavigator.Views.Filters;

using Ninject;

namespace Juru.RevitPlugins.ClashNavigator.Services;
/// <summary>
/// Собирает окно поисковых наборов. Его открывают из двух мест: кнопкой на ленте и из выбора
/// наборов в окне проверок, - поэтому сборка ядра живёт здесь, а не в команде.
/// </summary>
internal static class SearchSetsWindowFactory {
    /// <param name="revitRepository">
    /// Готовый репозиторий, если он у вызывающей стороны уже есть. Второй экземпляр создавать
    /// нельзя без нужды: его конструктор в транзакции создаёт 3D-вид для коллизий.
    /// </param>
    public static FiltersWindow Create(UIApplication uiApplication, RevitRepository revitRepository = null) {
        // See the same call in FindClashesCommand.Execute - harmless to repeat when this factory
        // runs from there (SelectionViewModel.OpenSearchSets), required when SetFiltersCommand
        // is the only thing that has run so far this session.
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
        kernel.Bind<RevitEventHandler>()
            .ToSelf()
            .InSingletonScope();
        kernel.Bind<ParameterFilterProvider>()
            .ToSelf()
            .InSingletonScope();

        if(revitRepository is null) {
            kernel.Bind<RevitRepository>()
                .ToSelf()
                .InSingletonScope();
        } else {
            kernel.Bind<RevitRepository>()
                .ToConstant(revitRepository);
        }

        // Constructed directly, not bound as IConfigSerializer - see comment in GetClashesCommand.
        kernel.Bind<SettingsConfig>()
            .ToMethod(c => SettingsConfig.GetSettingsConfig(new dosymep.Serializers.ConfigSerializer()));
        kernel.Bind<FiltersConfig>()
            .ToMethod(c => {
                var repository = c.Kernel.Get<RevitRepository>();
                return FiltersConfig.GetFiltersConfig(repository.GetConfigRelativePath(), repository.Doc);
            })
            .InSingletonScope();

        // WpfUIMessageBoxService requires these services during construction.
        kernel.UseWpfWindowsTheme();
        kernel.UsePluginLanguage();
        kernel.UseWpfUIThemeUpdater();

        kernel.UseWpfOpenFileDialog<FiltersViewModel>(filter: "ClashConfig (*.json)|*.json");
        kernel.UseWpfSaveFileDialog<FiltersViewModel>();
        kernel.UseWpfUIMessageBox<FiltersViewModel>();

        kernel.Bind<FiltersViewModel>()
            .ToSelf()
            .InSingletonScope();
        kernel.Bind<FiltersWindow>()
            .ToSelf()
            .InSingletonScope()
            .WithPropertyValue(nameof(Window.DataContext), c => c.Kernel.Get<FiltersViewModel>());
        // WpfUIMessageBoxService requires IHasTheme/IHasLocalization to be resolvable - FiltersWindow provides them.
        kernel.Bind<IHasTheme>()
            .ToMethod(c => c.Kernel.Get<FiltersWindow>());
        kernel.Bind<IHasLocalization>()
            .ToMethod(c => c.Kernel.Get<FiltersWindow>());

        string assemblyName = Assembly.GetExecutingAssembly().GetName().Name;
        kernel.UseWpfLocalization(
            $"/{assemblyName};component/assets/Localization/Language.xaml",
            CultureInfo.GetCultureInfo("ru-RU"));

        return kernel.Get<FiltersWindow>();
    }
}
