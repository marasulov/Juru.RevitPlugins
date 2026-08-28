using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

using Juru.RevitPlugins.Openings.ViewModels.Navigator;

namespace Juru.RevitPlugins.Openings.Views.Navigator;

/// <summary>
/// Окно навигатора согласования заданий на отверстия.
/// </summary>
/// <remarks>
/// Показать/Согласовать/Отклонить нарочно вызываются из code-behind по <c>Click</c>, а не через
/// <see cref="System.Windows.Input.ICommand"/>: команда с <c>CommandParameter="{Binding}"</c> внутри
/// ячейки грида в этом окне раз за разом залипала на первом (часто ещё не до конца привязанном)
/// значении <c>CanExecute</c> и переставала реагировать на дальнейшие изменения без явного
/// <c>NotifyCanExecuteChanged</c> - see the memory note from 2026-08-25. Доступность кнопок остаётся
/// обычной привязкой <c>IsEnabled</c> к свойству строки, без прослойки ICommand.
/// </remarks>
public partial class IncomingOpeningsNavigatorWindow : Window {
    public IncomingOpeningsNavigatorWindow() {
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
            || e.Item is not IncomingOpeningTaskViewModel row
            || row.SearchIndex.Contains(search, StringComparison.CurrentCultureIgnoreCase);
    }

    private void ShowButton_Click(object sender, RoutedEventArgs e) {
        if(GetRow(sender) is { } row && DataContext is IncomingOpeningsNavigatorViewModel vm) {
            vm.Show(row);
        }
    }

    private void ShowHostButton_Click(object sender, RoutedEventArgs e) {
        if(GetRow(sender) is { } row && DataContext is IncomingOpeningsNavigatorViewModel vm) {
            vm.ShowHost(row);
        }
    }

    private void InsertButton_Click(object sender, RoutedEventArgs e) {
        if(GetRow(sender) is { } row && DataContext is IncomingOpeningsNavigatorViewModel vm) {
            vm.Insert(row);
        }
    }

    private void ApproveButton_Click(object sender, RoutedEventArgs e) {
        if(GetRow(sender) is { } row && DataContext is IncomingOpeningsNavigatorViewModel vm) {
            vm.Approve(row);
        }
    }

    private void RejectButton_Click(object sender, RoutedEventArgs e) {
        if(GetRow(sender) is { } row && DataContext is IncomingOpeningsNavigatorViewModel vm) {
            vm.Reject(row);
        }
    }

    private static IncomingOpeningTaskViewModel GetRow(object sender) {
        return (sender as FrameworkElement)?.DataContext as IncomingOpeningTaskViewModel;
    }

    /// <summary>
    /// Классическая особенность <see cref="DataGrid"/>: первый клик по невыделенной ячейке уходит
    /// на выбор самой ячейки, а не на кнопку внутри неё - до кнопки доходит только второй клик.
    /// Фокусируем ячейку сразу в Preview-обработчике, чтобы тот же самый клик прошёл и до кнопки.
    /// </summary>
    private void DataGridCell_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        if(sender is DataGridCell { IsFocused: false } cell) {
            cell.Focus();
        }
    }
}
