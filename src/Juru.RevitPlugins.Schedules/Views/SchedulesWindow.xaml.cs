using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

using Juru.RevitPlugins.Schedules.ViewModels;

namespace Juru.RevitPlugins.Schedules.Views;

/// <summary>
///     Выбор спецификаций для английского комплекта.
/// </summary>
public partial class SchedulesWindow : Window
{
    public SchedulesWindow(SchedulesWindowViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;

        InitializeComponent();
    }

    public SchedulesWindowViewModel ViewModel { get; }

    /// <summary>
    ///     Строки, отмеченные пользователем, — пусто, если окно закрыли отменой.
    /// </summary>
    public IReadOnlyList<ScheduleRowViewModel> Result { get; private set; } = [];

    /// <summary>
    ///     Переключение галочки в строке, входящей в выделение, применяется ко всему выделению: иначе
    ///     отметить диапазон и снять его одним движением невозможно.
    /// </summary>
    private void CheckBox_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not CheckBox { IsChecked: { } isChecked, DataContext: ScheduleRowViewModel row })
        {
            return;
        }

        var selected = SchedulesGrid.SelectedItems.OfType<ScheduleRowViewModel>().ToList();

        ViewModel.ApplySelectionTo(selected.Contains(row) ? selected : [row], isChecked);
    }

    /// <summary>
    ///     То же правило для таблицы переводов: галочка внутри выделения применяется ко всему выделению.
    /// </summary>
    private void TranslationCheckBox_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not CheckBox { IsChecked: { } isChecked, DataContext: TranslationRowViewModel row })
        {
            return;
        }

        var selected = TranslationsGrid.SelectedItems.OfType<TranslationRowViewModel>().ToList();

        foreach (var target in selected.Contains(row) ? selected : [row])
        {
            target.IsSelected = isChecked;
        }
    }

    private void ButtonBuild_Click(object sender, RoutedEventArgs eventArgs)
    {
        Result = ViewModel.SelectedRows;

        if (Result.Count is 0)
        {
            MessageBox.Show(
                this,
                "Не отмечено ни одной спецификации.",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        DialogResult = true;
    }

    private void ButtonCancel_Click(object sender, RoutedEventArgs eventArgs)
    {
        DialogResult = false;
    }
}
