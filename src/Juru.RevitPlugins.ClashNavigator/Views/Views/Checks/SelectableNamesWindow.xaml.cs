using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;

using dosymep.SimpleServices;

using Juru.RevitPlugins.ClashNavigator.Resources;
using Juru.RevitPlugins.ClashNavigator.ViewModels.ClashDetective;

namespace Juru.RevitPlugins.ClashNavigator.Views.Checks;
/// <summary>
/// Диалог выбора из списка имён - файлов, поисковых наборов, категорий.
/// </summary>
/// <remarks>
/// Обычное модальное окно, а не <c>ui:ContentDialog</c>: у окна есть системный заголовок с
/// крестиком и Esc, поэтому закрыть его можно всегда, даже если тема WPF-UI в этом окне
/// не поднялась и кнопки диалога остались невидимыми.
/// </remarks>
internal partial class SelectableNamesWindow : Window {
    /// <param name="addButtonText">
    /// Подпись кнопки, которая закрывает диалог с <see cref="AddRequested" />, чтобы вызывающая
    /// сторона могла добавить в список то, чего в нём не хватает. Без подписи кнопки нет.
    /// </param>
    public SelectableNamesWindow(SelectableNamesViewModel viewModel,
        ILocalizationService localizationService,
        string addButtonText = null) {

        InitializeComponent();

        // Merging CustomStyles.xaml alone is not enough: WPF-UI controls (ui:Button, ui:TitleBar,
        // the CheckBox restyle it merges) render as plain unstyled controls until the library's
        // own theme is applied to this window. FluentWindow does this for itself internally;
        // a plain Window - used here so the title bar keeps its native close/Esc behavior - has
        // to call the library's public API itself.
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(this);

        CheckBoxBrushes.OverrideCheckedStates(this);

        ViewModel = viewModel;
        DataContext = viewModel;

        // Подписи ставятся из кода, а не через me:LocalizationSource: та разметка работает
        // только в окнах, реализующих IHasLocalization, а это обычный Window.
        SelectAllCheckBox.Content = localizationService.GetLocalizedString("Common.SelectAll");
        OkButton.Content = localizationService.GetLocalizedString("Common.Ok");
        CancelButton.Content = localizationService.GetLocalizedString("Common.Cancel");

        if(string.IsNullOrEmpty(addButtonText)) {
            AddButton.Visibility = System.Windows.Visibility.Collapsed;
        } else {
            AddButton.Content = addButtonText;
        }

        // Владелец - главное окно Ревита: иначе диалог уходит за него.
        _ = new WindowInteropHelper(this) { Owner = Process.GetCurrentProcess().MainWindowHandle };
    }

    public SelectableNamesViewModel ViewModel { get; }

    /// <summary>
    /// Диалог закрыт кнопкой добавления, а не выбором. Выбор при этом не применяется:
    /// список всё равно предстоит пересобрать.
    /// </summary>
    public bool AddRequested { get; private set; }

    private void ButtonOk_Click(object sender, RoutedEventArgs e) {
        DialogResult = true;
    }

    private void ButtonAdd_Click(object sender, RoutedEventArgs e) {
        AddRequested = true;
        DialogResult = false;
    }
}
