using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

using Juru.RevitPlugins.Openings.ViewModels.Navigator;

namespace Juru.RevitPlugins.Openings.Views.Navigator;

/// <summary>
/// Окно навигатора по заданиям на отверстия.
/// </summary>
/// <remarks>
/// "Показать"/"Хост" вызываются из code-behind по <c>Click</c>, а не через <see cref="ICommand"/>
/// с <c>CommandParameter="{Binding}"</c> внутри <see cref="DataGridTemplateColumn"/> - та же ловушка,
/// что уже чинили во входящем навигаторе (см. его code-behind): такая привязка раз за разом
/// проверяет <c>CanExecute</c> раньше, чем <c>CommandParameter</c> успевает разрешиться, и застревает
/// выключенной. Доступность решает обычная привязка <c>IsEnabled</c> к свойству строки.
/// </remarks>
public partial class OpeningsNavigatorWindow : Window {
    public OpeningsNavigatorWindow() {
        InitializeComponent();
    }

    private void ButtonClose_Click(object sender, RoutedEventArgs e) {
        Close();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) {
        (Resources["TasksView"] as CollectionViewSource)?.View?.Refresh();
    }

    private void TasksView_Filter(object sender, FilterEventArgs e) {
        string search = SearchBox.Text;
        e.Accepted = string.IsNullOrWhiteSpace(search)
            || e.Item is not OpeningTaskViewModel row
            || row.SearchIndex.Contains(search, StringComparison.CurrentCultureIgnoreCase);
    }

    private void ShowButton_Click(object sender, RoutedEventArgs e) {
        if(GetRow(sender) is { } row && DataContext is OpeningsNavigatorViewModel vm) {
            vm.Show(row);
        }
    }

    private void ShowHostButton_Click(object sender, RoutedEventArgs e) {
        if(GetRow(sender) is { } row && DataContext is OpeningsNavigatorViewModel vm) {
            vm.ShowHost(row);
        }
    }

    private static OpeningTaskViewModel? GetRow(object sender) {
        return (sender as FrameworkElement)?.DataContext as OpeningTaskViewModel;
    }

    /// <summary>
    /// Тот же приём, что во входящем навигаторе: первый клик по невыделенной ячейке иначе уходит
    /// на выбор самой ячейки, а не на кнопку внутри неё.
    /// </summary>
    private void DataGridCell_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        if(sender is DataGridCell { IsFocused: false } cell) {
            cell.Focus();
        }
    }
}
