using System.Windows;
using System.Windows.Controls;

using Juru.RevitPlugins.Core.Resources;

using Juru.RevitPlugins.Schedules.Localization;
using Juru.RevitPlugins.Schedules.ViewModels;

using Wpf.Ui.Appearance;

namespace Juru.RevitPlugins.Schedules.Views;

/// <summary>
///     Настройки перевода: служба, ключи, языки и вид записываемого значения.
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;

        InitializeComponent();

        // То же, что и в главном окне: простое Window обязано поднять тему WPF-UI само.
        ApplicationThemeManager.Apply(this);

        CheckBoxBrushes.OverrideCheckedStates(this);
        AccentButtonBrushes.Override(this);

        SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");
        SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
    }

    public SettingsViewModel ViewModel { get; }

    /// <summary>
    ///     Пароль не привязывается: <see cref="PasswordBox" /> намеренно не отдаёт его как свойство
    ///     для привязки, чтобы ключ не оставался в памяти привязки дольше нужного.
    /// </summary>
    private void DeepLKeyBox_PasswordChanged(object sender, RoutedEventArgs eventArgs)
    {
        ViewModel.DeepLKey = DeepLKeyBox.Password;
    }

    private void GoogleKeyBox_PasswordChanged(object sender, RoutedEventArgs eventArgs)
    {
        ViewModel.GoogleKey = GoogleKeyBox.Password;
    }

    private void ButtonSave_Click(object sender, RoutedEventArgs eventArgs)
    {
        ViewModel.SaveKeys();

        DeepLKeyBox.Clear();
        GoogleKeyBox.Clear();

        DialogResult = true;
    }

    private void ButtonCancel_Click(object sender, RoutedEventArgs eventArgs)
    {
        DialogResult = false;
    }

    private void ButtonDeepLHelp_Click(object sender, RoutedEventArgs eventArgs)
    {
        MessageBox.Show(this, Text.SettingsDeepLHelpBody, Text.SettingsDeepLHelpTitle,
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ButtonGoogleHelp_Click(object sender, RoutedEventArgs eventArgs)
    {
        MessageBox.Show(this, Text.SettingsGoogleHelpBody, Text.SettingsGoogleHelpTitle,
            MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
