using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

using Juru.RevitPlugins.Core.Localization;
using Juru.RevitPlugins.Openings.Models.Configs;
using Juru.RevitPlugins.Openings.ViewModels.Navigator;

namespace Juru.RevitPlugins.Openings.Views.Navigator;

/// <summary>
/// Окно навигатора по заданиям на отверстия.
/// </summary>
/// <remarks>
/// Кнопка "Хост" вызывается из code-behind по <c>Click</c>, а не через <see cref="ICommand"/>
/// с <c>CommandParameter="{Binding}"</c> внутри <see cref="DataGridTemplateColumn"/> - та же ловушка,
/// что уже чинили во входящем навигаторе (см. его code-behind): такая привязка раз за разом
/// проверяет <c>CanExecute</c> раньше, чем <c>CommandParameter</c> успевает разрешиться, и застревает
/// выключенной. Доступность решает обычная привязка <c>IsEnabled</c> к свойству строки.
/// Само задание показывает двойной щелчок по строке - отдельной кнопки "Показать" нет.
/// </remarks>
public partial class OpeningsNavigatorWindow : Window {
    /// <summary>
    /// Скрытые колонки, запомненные с прошлого раза.
    /// </summary>
    private readonly OpeningsNavigatorLayout _layout = OpeningsNavigatorLayout.Load();
    private readonly PluginLocalization _localization;

    /// <summary>
    /// Окно собрано целиком.
    /// </summary>
    /// <remarks>
    /// Обработчики из разметки стреляют уже во время <c>InitializeComponent</c>: поиск сообщает
    /// о своём начальном значении, когда элементы ниже по разметке - счётчик заданий - ещё
    /// не созданы, и обращение к ним падает с <c>NullReferenceException</c> прямо при открытии
    /// окна (та же ловушка, что во входящем навигаторе). До конца сборки обработчики молчат,
    /// а начальное состояние окно наводит само, в конце конструктора.
    /// </remarks>
    private bool _initialized;

    public OpeningsNavigatorWindow(PluginLocalization localization) {
        _localization = localization;
        InitializeComponent();
        localization.ApplyTo(this);
        // Заголовки колонок раздаются здесь, до сборки флажков: та читает Header, а заголовки
        // грида не входят в визуальное дерево и {DynamicResource} сами по себе не подхватывают.
        ColumnLocalization.Apply(TasksGrid, localization);
        BuildColumnToggles();

        _initialized = true;
        UpdatePosition();
    }

    /// <summary>
    /// Собирает флажки показа колонок по самому гриду.
    /// </summary>
    /// <remarks>
    /// По гриду, а не отдельным списком в XAML: второй перечень колонок пришлось бы править вместе
    /// с первым, и он отстал бы от него при первой же новой колонке.
    /// </remarks>
    private void BuildColumnToggles() {
        var style = TryFindResource("ColumnsPopupCheckBoxStyle") as Style;

        foreach(var column in TasksGrid.Columns) {
            var target = column;
            // Ключ локализации, а не заголовок: тот меняется с языком, и сохранённая видимость
            // колонок иначе слетала бы при каждом переключении языка плагина.
            string key = ColumnLocalization.GetKey(column) ?? column.Header?.ToString() ?? string.Empty;
            string header = column.Header?.ToString() ?? string.Empty;
            bool visible = !_layout.HiddenColumns.Contains(key);
            target.Visibility = visible ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

            var toggle = new CheckBox {
                Content = header,
                IsChecked = visible,
                Margin = new Thickness(0, 0, 10, 0)
            };
            if(style is not null) {
                toggle.Style = style;
            }

            toggle.Checked += (_, _) => SetColumnVisible(target, key, visible: true);
            toggle.Unchecked += (_, _) => SetColumnVisible(target, key, visible: false);
            ColumnsPanel.Children.Add(toggle);
        }
    }

    private void SetColumnVisible(DataGridColumn column, string key, bool visible) {
        column.Visibility = visible ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        if(!_initialized) {
            return;
        }

        if(visible) {
            _layout.HiddenColumns.Remove(key);
        } else if(!_layout.HiddenColumns.Contains(key)) {
            _layout.HiddenColumns.Add(key);
        }

        _layout.Save();
    }

    private void PreviousButton_Click(object sender, RoutedEventArgs e) {
        Move(forward: false);
    }

    private void NextButton_Click(object sender, RoutedEventArgs e) {
        Move(forward: true);
    }

    /// <summary>
    /// Переводит выделение на соседнее задание в том порядке, в каком список показан (с учётом
    /// поиска, сортировки и группировки), прокручивает к нему и показывает его в модели.
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

        if(row is OpeningTaskViewModel task && DataContext is OpeningsNavigatorViewModel vm) {
            vm.Show(task);
        }
    }

    private void UpdatePosition() {
        if(!_initialized) {
            return;
        }

        int index = TasksGrid.Items.IndexOf(TasksGrid.SelectedItem);
        PositionText.Text = index < 0
            ? string.Empty
            : _localization.GetString("Navigator.PositionFormat", index + 1, TasksGrid.Items.Count);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e) {
        DeleteSelected();
    }

    /// <summary>
    /// Delete по гриду - то же удаление: список читают клавишами, и тянуться к кнопке после каждой
    /// найденной пачки неудобно. <c>CanUserDeleteRows="False"</c> оставляет клавишу свободной -
    /// сам грид на неё ничего не делает.
    /// </summary>
    private void TasksGrid_PreviewKeyDown(object sender, KeyEventArgs e) {
        if(e.Key != Key.Delete) {
            return;
        }

        e.Handled = true;
        DeleteSelected();
    }

    /// <summary>
    /// Отдаёт во ViewModel выделенные строки: <c>DataGrid.SelectedItems</c> не привязывается,
    /// поэтому множественное выделение доходит до неё только отсюда.
    /// </summary>
    private void DeleteSelected() {
        if(DataContext is not OpeningsNavigatorViewModel vm) {
            return;
        }

        vm.Delete(TasksGrid.SelectedItems.OfType<OpeningTaskViewModel>().ToList());
    }

    /// <summary>
    /// Номер задания в списке должен следовать за выделением, кем бы оно ни менялось: щелчком
    /// по строке, кнопками перехода или удалением, после которого ViewModel встаёт на первое
    /// оставшееся задание уже без участия окна.
    /// </summary>
    private void TasksGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        UpdatePosition();
    }

    private void ButtonClose_Click(object sender, RoutedEventArgs e) {
        Close();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) {
        if(!_initialized) {
            return;
        }

        (Resources["TasksView"] as CollectionViewSource)?.View?.Refresh();

        // Номер задания считается по видимым строкам - после поиска их стало меньше.
        UpdatePosition();
    }

    private void TasksView_Filter(object sender, FilterEventArgs e) {
        string? search = SearchBox?.Text;
        e.Accepted = string.IsNullOrWhiteSpace(search)
            || e.Item is not OpeningTaskViewModel row
            || row.SearchIndex.Contains(search, StringComparison.CurrentCultureIgnoreCase);
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
