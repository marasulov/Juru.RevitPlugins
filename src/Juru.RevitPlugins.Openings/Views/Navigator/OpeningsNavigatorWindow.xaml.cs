using System.Windows;

namespace Juru.RevitPlugins.Openings.Views.Navigator;

/// <summary>
/// Окно навигатора по заданиям на отверстия.
/// </summary>
public partial class OpeningsNavigatorWindow : Window {
    public OpeningsNavigatorWindow() {
        InitializeComponent();
    }

    private void ButtonClose_Click(object sender, RoutedEventArgs e) {
        Close();
    }
}
