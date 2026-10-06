using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;

using dosymep.SimpleServices;

namespace Juru.RevitPlugins.ClashNavigator.Views;
/// <summary>
/// Окно сообщения в стиле плагина - вопрос "Да/Нет" или ошибка с одной кнопкой "ОК".
/// </summary>
/// <remarks>
/// Плагин не поднимает во всех своих окнах ту же тему WPF-UI, что и главное окно Ревита:
/// <c>IMessageBoxService</c> в окне проверок и поисковых наборов рисовал пустую плашку без
/// текста и кнопок, которую нечем было закрыть. Системный <c>MessageBox</c> закрывался всегда,
/// но выглядел чужеродно на фоне остальных окон плагина. Это окно - обычный <c>Window</c> с
/// <c>ApplicationThemeManager.Apply(this)</c>: системная строка заголовка (крестик, Esc) даёт
/// надёжное закрытие, а стиль WPF-UI на кнопках и содержимом всё равно поднимается.
/// </remarks>
internal partial class MessageWindow : Window {
    private MessageWindow(string title, string message, ILocalizationService localizationService) {
        InitializeComponent();
        // See the same call in SelectableNamesWindow: a plain Window needs this explicit call
        // for WPF-UI controls to render themed instead of as plain unstyled WPF controls.
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(this);

        Title = title;
        MessageText.Text = message;

        // Подписи из кода: у обычного Window нет IHasLocalization, на котором держится
        // me:LocalizationSource в разметке. Без службы локализации (сообщения из
        // RevitRepository, где её нет) - подписи по умолчанию: окно должно закрываться всегда.
        YesButton.Content = localizationService?.GetLocalizedString("Common.Yes") is { Length: > 0 } yes ? yes : "Yes";
        NoButton.Content = localizationService?.GetLocalizedString("Common.No") is { Length: > 0 } no ? no : "No";
        OkButton.Content = localizationService?.GetLocalizedString("Common.Ok") is { Length: > 0 } ok ? ok : "OK";

        // Владелец - главное окно Ревита: иначе окно уходит за него.
        _ = new WindowInteropHelper(this) { Owner = Process.GetCurrentProcess().MainWindowHandle };
    }

    public static bool AskYesNo(string message, string title, ILocalizationService localizationService) {
        var window = new MessageWindow(title, message, localizationService);
        window.YesButton.Visibility = System.Windows.Visibility.Visible;
        window.NoButton.Visibility = System.Windows.Visibility.Visible;
        window.OkButton.Visibility = System.Windows.Visibility.Collapsed;
        return window.ShowDialog() == true;
    }

    /// <summary>
    /// Сообщение с одной кнопкой "ОК". То же окно, что и <see cref="ShowError" /> - отдельное имя
    /// нужно, чтобы на месте вызова было видно, ошибка это или обычное сообщение.
    /// </summary>
    public static void ShowInfo(string message, string title, ILocalizationService localizationService) {
        ShowError(message, title, localizationService);
    }

    public static void ShowError(string message, string title, ILocalizationService localizationService) {
        var window = new MessageWindow(title, message, localizationService);
        window.YesButton.Visibility = System.Windows.Visibility.Collapsed;
        window.NoButton.Visibility = System.Windows.Visibility.Collapsed;
        window.OkButton.Visibility = System.Windows.Visibility.Visible;
        window.ShowDialog();
    }

    private void ButtonYes_Click(object sender, RoutedEventArgs e) {
        DialogResult = true;
    }
}
