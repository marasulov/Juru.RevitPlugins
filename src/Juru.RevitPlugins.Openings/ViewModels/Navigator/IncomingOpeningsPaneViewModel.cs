using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Juru.RevitPlugins.Openings.Models.Server;

namespace Juru.RevitPlugins.Openings.ViewModels.Navigator;

/// <summary>
/// Панель согласования: те же задания, что в окне навигатора, но решение принимается по одному -
/// по выделенному в списке, с переходом по списку кнопками.
/// </summary>
/// <remarks>
/// Список, загрузка, отправка и прорезание целиком остаются за
/// <see cref="IncomingOpeningsNavigatorViewModel"/> - он лежит внутри как <see cref="Navigator"/>,
/// а не наследуется и не правится: у окна согласования свои правила решения (комментарий набирают
/// в ячейке, поэтому "Отклонить" там требует его заранее), и попытка обслужить оба набора правил
/// одним кодом уже один раз ломала и то, и другое.
/// </remarks>
public partial class IncomingOpeningsPaneViewModel : ObservableObject {
    /// <summary>
    /// Ключ выделенного задания - строки пересобираются при каждой загрузке и прорезании,
    /// и выделение нужно вернуть на то же задание, а не на то же место в списке.
    /// </summary>
    private string _selectedKey;

    [ObservableProperty]
    private IncomingOpeningTaskViewModel _selectedTask;

    /// <summary>
    /// Нажали "Отклонить" по заданию без причины: поле открыто, но решение ещё не принято.
    /// </summary>
    [ObservableProperty]
    private bool _isRejecting;

    /// <summary>
    /// "Задание N из M" рядом с кнопками перехода. Считает вью: номер зависит от того, что видно
    /// в списке после поиска и сортировки, а это знает только представление.
    /// </summary>
    [ObservableProperty]
    private string _positionText = string.Empty;

    public IncomingOpeningsPaneViewModel(IncomingOpeningsNavigatorViewModel navigator) {
        Navigator = navigator ?? throw new System.ArgumentNullException(nameof(navigator));
        ((INotifyCollectionChanged) Navigator.Tasks).CollectionChanged += OnTasksChanged;

        // Сводку и ошибки навигатор меняет у себя - панель показывает их как свои свойства,
        // поэтому переизвещает о них сама. Через путь "Navigator.Summary" в XAML это делать нельзя:
        // тот же путь в ресурсах (CollectionViewSource грида) молча не разрешался, и панель
        // оставалась пустой при полном списке заданий.
        Navigator.PropertyChanged += OnNavigatorPropertyChanged;

        SelectedTask = Navigator.Tasks.FirstOrDefault();
    }

    /// <summary>
    /// Всё, что панель делит с окном согласования: список заданий, сводка, ошибки, кнопки
    /// "Обновить", "Сохранить" и "Вставить все согласованные". XAML привязывается через него.
    /// </summary>
    public IncomingOpeningsNavigatorViewModel Navigator { get; }

    /// <summary>
    /// Задания - те же самые объекты строк, что у навигатора.
    /// </summary>
    public ObservableCollection<IncomingOpeningTaskViewModel> Tasks => Navigator.Tasks;

    public string Summary => Navigator.Summary;

    public string ErrorText => Navigator.ErrorText;

    public bool HasError => Navigator.HasError;

    public string CoordinateWarning => Navigator.CoordinateWarning;

    public bool HasCoordinateWarning => Navigator.HasCoordinateWarning;

    /// <summary>
    /// Перечитывает задания с сервера и заново ставит их в модели.
    /// </summary>
    public IRelayCommand RefreshCommand => Navigator.RefreshCommand;

    public IRelayCommand SaveCommand => Navigator.SaveCommand;

    public IRelayCommand InsertAllApprovedCommand => Navigator.InsertAllApprovedCommand;

    /// <summary>
    /// Панель решает по своим правилам: "Отклонить" доступна всегда, потому что причину здесь
    /// набирают уже после нажатия, а "Согласовать" - пока это решение не принято.
    /// </summary>
    public bool CanApproveSelected => SelectedTask is not null && !SelectedTask.IsApproved;

    public bool CanRejectSelected => SelectedTask is not null;

    /// <summary>
    /// Поле причины открыто: либо её сейчас набирают после "Отклонить", либо задание уже отклонено
    /// и причину правят. У согласованного задания причины быть не должно.
    /// </summary>
    public bool CanEditComment => SelectedTask is not null && (IsRejecting || SelectedTask.IsRejected);

    /// <summary>
    /// Что за задание сейчас в панели - шапка над кнопками решения: по одному списку не всегда
    /// видно, какая строка выделена, а решение принимается именно по ней.
    /// </summary>
    public string SelectedTaskTitle {
        get {
            var task = SelectedTask;
            return task is null
                ? "Задание не выбрано - выделите строку в списке"
                : $"{task.OpeningTypeText} {task.SizeText} мм, {task.LevelName}, {task.MepSystem} "
                    + $"(от {task.SenderDiscipline}, {task.SentByDisplayName}). Моё решение: {task.OwnStatusText}";
        }
    }

    /// <summary>
    /// Что сделает следующее нажатие - подсказка под кнопками решения.
    /// </summary>
    public string DecisionHint {
        get {
            if(SelectedTask is null) {
                return string.Empty;
            }

            return IsRejecting
                ? "Напишите причину и нажмите \"Отклонить\" ещё раз (или Enter в поле): "
                    + "сервер не принимает отклонение без комментария."
                : "Решение отмечается локально; на сервер уйдёт по кнопке \"Сохранить\".";
        }
    }

    /// <summary>
    /// Отмечает задание согласованным локально.
    /// </summary>
    /// <returns>Решение принято - вью может переходить к следующему заданию.</returns>
    public bool ApproveSelected() {
        var task = SelectedTask;
        if(task is null) {
            return false;
        }

        // Причина относилась к отклонению, от которого сейчас отказались: оставить её значило бы
        // отправить согласование с чужим текстом в поле.
        task.Comment = string.Empty;
        task.Status = OpeningDecisionStatus.Approved;
        IsRejecting = false;
        RefreshDecisionState();
        return true;
    }

    /// <summary>
    /// Первое нажатие по заданию без причины открывает поле, второе - принимает решение.
    /// </summary>
    /// <returns>
    /// Решение принято - вью может переходить к следующему заданию. <c>false</c>, если поле причины
    /// только что открылось и его нужно заполнить.
    /// </returns>
    public bool RejectSelected() {
        var task = SelectedTask;
        if(task is null) {
            return false;
        }

        if(string.IsNullOrWhiteSpace(task.Comment)) {
            IsRejecting = true;
            return false;
        }

        task.Status = OpeningDecisionStatus.Rejected;
        IsRejecting = false;
        RefreshDecisionState();
        return true;
    }

    partial void OnSelectedTaskChanged(IncomingOpeningTaskViewModel value) {
        // Режим отклонения относится к тому заданию, по которому его начали.
        IsRejecting = false;
        if(value is not null) {
            _selectedKey = value.TaskKey;
        }

        RefreshDecisionState();
    }

    partial void OnIsRejectingChanged(bool value) {
        RefreshDecisionState();
    }

    /// <summary>
    /// Список пересобрали (загрузка, отправка, прорезание) - возвращаем выделение на то же задание
    /// по ключу: объекты строк уже другие, а смотрит пользователь на то же отверстие.
    /// </summary>
    private void OnTasksChanged(object sender, NotifyCollectionChangedEventArgs e) {
        if(Navigator.Tasks.Count == 0) {
            SelectedTask = null;
            return;
        }

        if(SelectedTask is null || !Navigator.Tasks.Contains(SelectedTask)) {
            SelectedTask = Navigator.Tasks.FirstOrDefault(task => task.TaskKey == _selectedKey)
                ?? Navigator.Tasks[0];
        }
    }

    private void OnNavigatorPropertyChanged(object sender, PropertyChangedEventArgs e) {
        switch(e.PropertyName) {
            case nameof(IncomingOpeningsNavigatorViewModel.Summary):
                OnPropertyChanged(nameof(Summary));
                break;
            case nameof(IncomingOpeningsNavigatorViewModel.ErrorText):
                OnPropertyChanged(nameof(ErrorText));
                OnPropertyChanged(nameof(HasError));
                break;
            case nameof(IncomingOpeningsNavigatorViewModel.CoordinateWarning):
                OnPropertyChanged(nameof(CoordinateWarning));
                OnPropertyChanged(nameof(HasCoordinateWarning));
                break;
        }
    }

    private void RefreshDecisionState() {
        OnPropertyChanged(nameof(CanApproveSelected));
        OnPropertyChanged(nameof(CanRejectSelected));
        OnPropertyChanged(nameof(CanEditComment));
        OnPropertyChanged(nameof(SelectedTaskTitle));
        OnPropertyChanged(nameof(DecisionHint));
    }
}
