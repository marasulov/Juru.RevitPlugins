using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

using Juru.RevitPlugins.Openings.Models.Configs;
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
    /// <summary>
    /// Скрытые колонки и фильтр решений, запомненные с прошлого раза.
    /// </summary>
    private readonly IncomingOpeningsNavigatorLayout _layout = IncomingOpeningsNavigatorLayout.Load();

    /// <summary>
    /// Идёт восстановление запомненных настроек: обработчики флажков в это время не пересохраняют
    /// их и не дёргают фильтр - файл настроек иначе перезаписывался бы трижды подряд.
    /// </summary>
    private bool _restoring;

    public IncomingOpeningsNavigatorWindow() {
        InitializeComponent();
        RestoreLayout();
        BuildColumnToggles();
    }

    private void RestoreLayout() {
        _restoring = true;
        ShowPendingBox.IsChecked = _layout.ShowPending;
        ShowApprovedBox.IsChecked = _layout.ShowApproved;
        ShowRejectedBox.IsChecked = _layout.ShowRejected;
        _restoring = false;
    }

    /// <summary>
    /// Собирает флажки показа колонок по самому гриду.
    /// </summary>
    /// <remarks>
    /// По гриду, а не отдельным списком в XAML: второй перечень колонок пришлось бы править вместе
    /// с первым, и он отстал бы от него при первой же новой колонке.
    /// </remarks>
    private void BuildColumnToggles() {
        var style = TryFindResource("DefaultCheckBoxStyle") as Style;

        foreach(var column in TasksGrid.Columns) {
            var target = column;
            string header = column.Header?.ToString() ?? string.Empty;
            bool visible = !_layout.HiddenColumns.Contains(header);
            target.Visibility = visible ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

            var toggle = new CheckBox {
                Content = header,
                IsChecked = visible,
                Margin = new Thickness(0, 0, 10, 0)
            };
            if(style is not null) {
                toggle.Style = style;
            }

            toggle.Checked += (_, _) => SetColumnVisible(target, header, visible: true);
            toggle.Unchecked += (_, _) => SetColumnVisible(target, header, visible: false);
            ColumnsPanel.Children.Add(toggle);
        }
    }

    private void SetColumnVisible(DataGridColumn column, string header, bool visible) {
        column.Visibility = visible ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        if(visible) {
            _layout.HiddenColumns.Remove(header);
        } else if(!_layout.HiddenColumns.Contains(header)) {
            _layout.HiddenColumns.Add(header);
        }

        _layout.Save();
    }

    private void StatusFilter_Changed(object sender, RoutedEventArgs e) {
        if(_restoring) {
            return;
        }

        _layout.ShowPending = ShowPendingBox.IsChecked == true;
        _layout.ShowApproved = ShowApprovedBox.IsChecked == true;
        _layout.ShowRejected = ShowRejectedBox.IsChecked == true;
        _layout.Save();

        (Resources["TasksView"] as CollectionViewSource)?.View?.Refresh();
        UpdatePosition();
    }

    private void PreviousButton_Click(object sender, RoutedEventArgs e) {
        Move(forward: false);
    }

    private void NextButton_Click(object sender, RoutedEventArgs e) {
        Move(forward: true);
    }

    /// <summary>
    /// Переводит выделение на соседнее задание в том порядке, в каком список показан (с учётом
    /// поиска, фильтра, сортировки и группировки), прокручивает к нему и показывает его в модели.
    /// </summary>
    private void Move(bool forward) {
        var items = TasksGrid.Items;
        if(items.Count == 0) {
            return;
        }

        // Выделения может не быть вовсе - тогда первое нажатие встаёт на край списка, а не молчит:
        // молчащая кнопка выглядит как неработающая.
        int index = items.IndexOf(TasksGrid.SelectedItem);
        int next = index < 0
            ? (forward ? 0 : items.Count - 1)
            : index + (forward ? 1 : -1);
        if(next < 0 || next >= items.Count) {
            return;
        }

        var row = items[next];
        TasksGrid.SelectedItem = row;
        TasksGrid.ScrollIntoView(row);
        UpdatePosition();

        if(row is IncomingOpeningTaskViewModel task && DataContext is IncomingOpeningsNavigatorViewModel vm) {
            vm.Show(task);
        }
    }

    private void UpdatePosition() {
        int index = TasksGrid.Items.IndexOf(TasksGrid.SelectedItem);
        PositionText.Text = index < 0
            ? string.Empty
            : $"Задание {index + 1} из {TasksGrid.Items.Count}";
    }

    private void ButtonClose_Click(object sender, RoutedEventArgs e) {
        Close();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) {
        (Resources["TasksView"] as CollectionViewSource)?.View?.Refresh();

        // Номер задания считается по видимым строкам - после поиска их стало меньше.
        UpdatePosition();
    }

    private void TasksView_Filter(object sender, FilterEventArgs e) {
        if(e.Item is not IncomingOpeningTaskViewModel row) {
            e.Accepted = true;
            return;
        }

        e.Accepted = MatchesSearch(row) && MatchesStatus(row);
    }

    private bool MatchesSearch(IncomingOpeningTaskViewModel row) {
        string search = SearchBox.Text;
        return string.IsNullOrWhiteSpace(search)
            || row.SearchIndex.Contains(search, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>
    /// Фильтр по своему решению: оно видно в колонке "Моё решение", в отличие от общего статуса
    /// задания, который считает сервер из решений обеих дисциплин.
    /// </summary>
    private bool MatchesStatus(IncomingOpeningTaskViewModel row) {
        if(row.IsApproved) {
            return ShowApprovedBox.IsChecked == true;
        }

        if(row.IsRejected) {
            return ShowRejectedBox.IsChecked == true;
        }

        return ShowPendingBox.IsChecked == true;
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
