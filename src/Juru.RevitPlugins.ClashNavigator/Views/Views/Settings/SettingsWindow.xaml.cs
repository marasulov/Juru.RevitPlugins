using System.Windows;

namespace Juru.RevitPlugins.ClashNavigator.Views.Settings;
public partial class SettingsWindow : Window {
    public SettingsWindow() {
        InitializeComponent();
        // Без этого отмеченный флажок пропадает под курсором - см. CheckBoxBrushes.
        ClashNavigator.Resources.CheckBoxBrushes.OverrideCheckedStates(this);
    }
    private void ButtonOk_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void ButtonCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
