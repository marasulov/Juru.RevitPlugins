using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;

using Juru.RevitPlugins.ClashNavigator.Services;

namespace Juru.RevitPlugins.ClashNavigator.Views;
/// <summary>
/// Спрашивает язык интерфейса при первом открытии плагина.
/// </summary>
internal partial class LanguagePromptWindow : Window {
    private LanguagePromptWindow() {
        InitializeComponent();
        // См. тот же вызов в SelectableNamesWindow: обычному Window тему WPF-UI надо применить явно.
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(this);

        // Владелец - главное окно Ревита: иначе окно уходит за него.
        _ = new WindowInteropHelper(this) { Owner = Process.GetCurrentProcess().MainWindowHandle };
    }

    /// <summary>
    /// Возвращает выбранный язык или <c>null</c>, если окно закрыли, ничего не выбрав.
    /// </summary>
    public static string Ask() {
        var window = new LanguagePromptWindow();
        return window.ShowDialog() == true ? window.Language : null;
    }

    private string Language { get; set; }

    private void ButtonRussian_Click(object sender, RoutedEventArgs e) {
        Language = PluginLanguageService.Russian;
        DialogResult = true;
    }

    private void ButtonEnglish_Click(object sender, RoutedEventArgs e) {
        Language = PluginLanguageService.English;
        DialogResult = true;
    }
}
