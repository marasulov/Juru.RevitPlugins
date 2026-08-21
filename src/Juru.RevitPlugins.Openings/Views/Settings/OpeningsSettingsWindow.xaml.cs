using System.Windows;

namespace Juru.RevitPlugins.Openings.Views.Settings;

/// <summary>
/// Окно настроек выдачи заданий на отверстия.
/// </summary>
public partial class OpeningsSettingsWindow : Window {
    public OpeningsSettingsWindow() {
        InitializeComponent();
    }

    private void ButtonOk_Click(object sender, RoutedEventArgs e) {
        DialogResult = true;
    }

    private void ButtonCancel_Click(object sender, RoutedEventArgs e) {
        DialogResult = false;
    }
}
