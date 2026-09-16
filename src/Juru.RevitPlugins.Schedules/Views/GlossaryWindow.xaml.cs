using System.Windows;

using Juru.RevitPlugins.Core.Resources;

using Juru.RevitPlugins.Schedules.ViewModels;

using Wpf.Ui.Appearance;

namespace Juru.RevitPlugins.Schedules.Views;

/// <summary>
///     Весь словарь переводов: правка вручную и свои строки.
/// </summary>
public partial class GlossaryWindow : Window
{
    public GlossaryWindow(GlossaryWindowViewModel viewModel)
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

    public GlossaryWindowViewModel ViewModel { get; }

    /// <summary>Итог сохранения: главное окно показывает его в своей строке состояния.</summary>
    public string? Note { get; private set; }

    private void ButtonSave_Click(object sender, RoutedEventArgs eventArgs)
    {
        // Правка последней ячейки остаётся в редакторе таблицы, пока его не закрыть: без этого
        // то, что набрали и сразу нажали «Сохранить», в словарь не попадает.
        Grid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);

        Note = ViewModel.Save();

        DialogResult = true;
    }

    private void ButtonCancel_Click(object sender, RoutedEventArgs eventArgs)
    {
        DialogResult = false;
    }
}
