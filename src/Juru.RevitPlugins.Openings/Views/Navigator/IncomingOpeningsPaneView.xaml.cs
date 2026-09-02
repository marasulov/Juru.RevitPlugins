using System;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

using Juru.RevitPlugins.Openings.ViewModels.Navigator;

namespace Juru.RevitPlugins.Openings.Views.Navigator;

/// <summary>
/// Содержимое закреплённой панели согласования: список заданий и панель решения под ним.
/// </summary>
/// <remarks>
/// Переход по списку и номер задания живут здесь, а не в модели представления: порядок задаёт
/// представление списка (поиск и сортировка грида), а не коллекция модели. Кнопки вызывают
/// обработчики <c>Click</c> напрямую - решение принимает модель представления, а переход к
/// следующему заданию окно делает само.
/// </remarks>
public partial class IncomingOpeningsPaneView : UserControl {
    /// <summary>
    /// Отсортированный и отфильтрованный поиском список - именно он привязан к гриду.
    /// </summary>
    /// <remarks>
    /// Задаётся кодом, а не <c>CollectionViewSource</c> в ресурсах: ресурс не входит в дерево
    /// элементов, привязка его <c>Source</c> к свойству модели представления не разрешилась, и грид
    /// оставался пустым при полном списке заданий - выглядело это как "ничего не грузится".
    /// </remarks>
    private CollectionViewSource _tasksView;

    public IncomingOpeningsPaneView() {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private IncomingOpeningsPaneViewModel ViewModel => DataContext as IncomingOpeningsPaneViewModel;

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e) {
        if(ViewModel is not { } viewModel) {
            _tasksView = null;
            TasksGrid.ItemsSource = null;
            return;
        }

        // Порядок как в окне навигатора: проблемные конструкции первыми, дальше по системе.
        // Группировки здесь нет - в панели идут по списку подряд, и раскрытые группы этому мешают.
        _tasksView = new CollectionViewSource { Source = viewModel.Tasks };
        _tasksView.SortDescriptions.Add(new SortDescription(
            nameof(IncomingOpeningTaskViewModel.TechnicalSortOrder), ListSortDirection.Ascending));
        _tasksView.SortDescriptions.Add(new SortDescription(
            nameof(IncomingOpeningTaskViewModel.MepSystem), ListSortDirection.Ascending));
        _tasksView.Filter += TasksView_Filter;

        TasksGrid.ItemsSource = _tasksView.View;
        UpdatePosition();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) {
        _tasksView?.View?.Refresh();

        // Номер задания считается по видимым строкам - после поиска их стало меньше.
        UpdatePosition();
    }

    private void TasksView_Filter(object sender, FilterEventArgs e) {
        string search = SearchBox.Text;
        e.Accepted = string.IsNullOrWhiteSpace(search)
            || e.Item is not IncomingOpeningTaskViewModel row
            || row.SearchIndex.Contains(search, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>
    /// Выделили строку - показываем это задание в модели. Отдельной кнопки "Показать" в панели нет:
    /// задание нужно видеть всякий раз, когда его рассматривают.
    /// </summary>
    private void TasksGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        // Грид сбрасывает SelectedItem в null, когда ему (пере)привязывают список, и через
        // двустороннюю привязку пишет этот null в модель. Возвращаем выделение на первую строку,
        // иначе панель остаётся без задания при непустом списке.
        if(TasksGrid.SelectedItem is null && TasksGrid.Items.Count > 0) {
            TasksGrid.SelectedIndex = 0;
            return;
        }

        UpdatePosition();

        if(TasksGrid.SelectedItem is IncomingOpeningTaskViewModel task) {
            ViewModel?.Navigator.Show(task);
        }
    }

    private void PreviousButton_Click(object sender, System.Windows.RoutedEventArgs e) {
        Move(forward: false);
    }

    private void NextButton_Click(object sender, System.Windows.RoutedEventArgs e) {
        Move(forward: true);
    }

    /// <summary>
    /// Кнопка "Согласовать": решение принимает модель представления, переход к следующему заданию -
    /// вью, потому что порядок задаёт грид.
    /// </summary>
    private void ApproveButton_Click(object sender, System.Windows.RoutedEventArgs e) {
        if(ViewModel?.ApproveSelected() == true) {
            Move(forward: true);
        }
    }

    /// <summary>
    /// Кнопка "Отклонить": первое нажатие по заданию без причины открывает поле и ставит в него
    /// курсор, второе - принимает решение и переходит к следующему заданию.
    /// </summary>
    private void RejectButton_Click(object sender, System.Windows.RoutedEventArgs e) {
        var viewModel = ViewModel;
        if(viewModel is null) {
            return;
        }

        if(viewModel.RejectSelected()) {
            Move(forward: true);
        } else if(viewModel.CanEditComment) {
            CommentBox.Focus();
        }
    }

    /// <summary>
    /// Enter в поле причины - то же, что повторное нажатие "Отклонить": набрал причину, отклонил,
    /// поехал дальше, не уводя руку с клавиатуры.
    /// </summary>
    private void CommentBox_KeyDown(object sender, KeyEventArgs e) {
        if(e.Key != Key.Enter) {
            return;
        }

        e.Handled = true;
        RejectButton_Click(sender, e);
    }

    /// <summary>
    /// Переводит выделение на соседнее задание в том порядке, в каком список показан, и прокручивает
    /// к нему: панель для того и сделана, чтобы идти по списку подряд. Показ в модели придёт из
    /// <see cref="TasksGrid_SelectionChanged"/>.
    /// </summary>
    private void Move(bool forward) {
        var items = TasksGrid.Items;
        if(items.Count == 0) {
            return;
        }

        // Выделения может не быть вовсе - и когда панель только открыли, и когда грид сбросил его
        // сам. Тогда первое нажатие встаёт на край списка, а не молчит: молчащая кнопка выглядит
        // как неработающая.
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
    }

    private void UpdatePosition() {
        if(ViewModel is not { } viewModel) {
            return;
        }

        int index = TasksGrid.Items.IndexOf(TasksGrid.SelectedItem);
        viewModel.PositionText = index < 0
            ? string.Empty
            : $"Задание {index + 1} из {TasksGrid.Items.Count}";
    }
}
