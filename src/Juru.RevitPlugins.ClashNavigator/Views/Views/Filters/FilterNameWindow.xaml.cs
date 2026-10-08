using System.Windows;

using dosymep.SimpleServices;

using Juru.RevitPlugins.ClashNavigator.ViewModels.SearchSet;

namespace Juru.RevitPlugins.ClashNavigator.Views.Filters;
/// <summary>
/// Запрос имени поискового набора при переименовании и копировании.
/// </summary>
internal partial class FilterNameWindow : Window {
    public FilterNameWindow(FilterNameViewModel viewModel, ILocalizationService localizationService, Window owner) {
        InitializeComponent();

        // См. тот же вызов в SelectableNamesWindow: обычному Window тему WPF-UI надо применить явно.
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(this);

        DataContext = viewModel;
        Owner = owner;

        // Подписи из кода: me:LocalizationSource работает только в окнах с IHasLocalization.
        OkButton.Content = localizationService.GetLocalizedString("Common.Ok");
        CancelButton.Content = localizationService.GetLocalizedString("Common.Cancel");

        Loaded += (_, _) => {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private void ButtonOk_Click(object sender, RoutedEventArgs e) {
        DialogResult = true;
    }
}
