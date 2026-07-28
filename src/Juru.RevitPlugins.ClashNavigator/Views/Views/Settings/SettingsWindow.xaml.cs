using System.Windows;

namespace Juru.RevitPlugins.ClashNavigator.Views.Settings;
public partial class SettingsWindow : Window {
    public SettingsWindow() => InitializeComponent();
    private void ButtonOk_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void ButtonCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
