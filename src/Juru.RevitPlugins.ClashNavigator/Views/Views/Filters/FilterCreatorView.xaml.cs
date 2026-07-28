using System.Windows;

namespace Juru.RevitPlugins.ClashNavigator.Views.Filters;
public partial class FilterCreatorView : Window {
    public FilterCreatorView() => InitializeComponent();
    private void ButtonOk_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void ButtonCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
