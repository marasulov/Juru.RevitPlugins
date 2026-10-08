using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

using dosymep.Bim4Everyone;
using dosymep.SimpleServices;
using dosymep.WPF.Commands;
using dosymep.WPF.ViewModels;

using Juru.RevitPlugins.ClashNavigator.Models;
using Juru.RevitPlugins.ClashNavigator.Models.FilterModel;
using Juru.RevitPlugins.ClashNavigator.Services;
using Juru.RevitPlugins.ClashNavigator.Services.NavisworksImport;
using Juru.RevitPlugins.ClashNavigator.ViewModels.Services;

using Newtonsoft.Json;

using Wpf.Ui;

namespace Juru.RevitPlugins.ClashNavigator.ViewModels.ClashDetective;
internal class MainViewModel : BaseViewModel {
    private string _errorText;

    private readonly ChecksConfig _checksConfig;
    private readonly FiltersConfig _filtersConfig;
    private readonly RevitRepository _revitRepository;
    private readonly ILocalizationService _localization;
    private readonly IContentDialogService _contentDialogService;
    private readonly SettingsConfig _settingsConfig;
    private bool _canCancel = true;
    private string _messageText;
    private ObservableCollection<CheckViewModel> _allChecks;
    private string _savedState;
    private CheckViewModel _selectedCheck;
    private CollectionViewSource _checks;
    private string _checksFilter;

    public MainViewModel(
        RevitRepository revitRepository,
        ILocalizationService localizationService,
        IOpenFileDialogService openFileDialogService,
        ISaveFileDialogService saveFileDialogService,
        IMessageBoxService messageBoxService,
        IContentDialogService contentDialogService,
        SettingsConfig settingsConfig,
        ChecksConfig checksConfig,
        FiltersConfig filtersConfig) {

        _filtersConfig = filtersConfig ?? throw new ArgumentNullException(nameof(filtersConfig));
        _revitRepository = revitRepository ?? throw new ArgumentNullException(nameof(revitRepository));
        _localization = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        OpenFileDialogService = openFileDialogService ?? throw new ArgumentNullException(nameof(openFileDialogService));
        SaveFileDialogService = saveFileDialogService ?? throw new ArgumentNullException(nameof(saveFileDialogService));
        MessageBoxService = messageBoxService ?? throw new ArgumentNullException(nameof(messageBoxService));
        _contentDialogService = contentDialogService ?? throw new ArgumentNullException(nameof(contentDialogService));
        _settingsConfig = settingsConfig ?? throw new ArgumentNullException(nameof(settingsConfig));
        _checksConfig = checksConfig ?? throw new ArgumentNullException(nameof(checksConfig));

        if(_checksConfig != null && _checksConfig.Checks.Count > 0) {
            InitializeChecks();
        } else {
            InitializeEmptyCheck();
        }
        AddCheckCommand = RelayCommand.Create(AddCheck);
        RemoveCheckCommand = RelayCommand.Create<IList>(RemoveCheck, CanRemove);
        FindClashesCommand = RelayCommand.Create(FindClashes, CanFindClashes);

        SaveClashesCommand = RelayCommand.Create(SaveConfig);
        SaveAsClashesCommand = RelayCommand.Create(SaveAsConfig);
        ExportNavisworksCommand = RelayCommand.Create(ExportNavisworks);
        LoadClashCommand = RelayCommand.Create(LoadConfig);
        AskForSaveCommand = RelayCommand.Create(AskForSave);

        PropertyChanged += ChecksFilterPropertyChanged;
        RememberSavedState();
    }

    public bool CanCancel {
        get => _canCancel;
        set => RaiseAndSetIfChanged(ref _canCancel, value);
    }

    public string ErrorText {
        get => _errorText;
        set => RaiseAndSetIfChanged(ref _errorText, value);
    }

    public string ChecksFilter {
        get => _checksFilter;
        set => RaiseAndSetIfChanged(ref _checksFilter, value);
    }

    public string MessageText {
        get => _messageText;
        set => RaiseAndSetIfChanged(ref _messageText, value);
    }

    public CheckViewModel SelectedCheck {
        get => _selectedCheck;
        set => RaiseAndSetIfChanged(ref _selectedCheck, value);
    }

    public ICommand AskForSaveCommand { get; }
    public ICommand AddCheckCommand { get; }
    public ICommand RemoveCheckCommand { get; }
    public ICommand FindClashesCommand { get; }
    public ICommand SaveAsClashesCommand { get; }
    public ICommand ExportNavisworksCommand { get; }
    public ICommand SaveClashesCommand { get; }
    public ICommand LoadClashCommand { get; }
    public IOpenFileDialogService OpenFileDialogService { get; }
    public ISaveFileDialogService SaveFileDialogService { get; }
    public IMessageBoxService MessageBoxService { get; }


    public CollectionViewSource Checks {
        get => _checks;
        private set => RaiseAndSetIfChanged(ref _checks, value);
    }

    private ObservableCollection<CheckViewModel> AllChecks {
        get => _allChecks;
        set => RaiseAndSetIfChanged(ref _allChecks, value);
    }

    private void InitializeChecks() {
        SetAllChecks(InitializeChecks(_checksConfig).OrderBy(c => c.Name));
    }

    private IEnumerable<CheckViewModel> InitializeChecks(ChecksConfig config) {
        foreach(var check in config.Checks) {
            yield return new CheckViewModel(_revitRepository,
                _localization,
                OpenFileDialogService,
                SaveFileDialogService,
                MessageBoxService,
                _settingsConfig,
                _filtersConfig,
                _contentDialogService,
                check);
        }
    }

    private void ChecksFilterPropertyChanged(object sender, PropertyChangedEventArgs e) {
        if(e.PropertyName == nameof(ChecksFilter)) {
            Checks?.View.Refresh();
            RaisePropertyChanged(nameof(AllChecksSelected));
        }
    }

    private void InitializeEmptyCheck() {
        SetAllChecks([
            new CheckViewModel(_revitRepository,
            _localization,
            OpenFileDialogService,
            SaveFileDialogService,
            MessageBoxService,
            _settingsConfig,
            _filtersConfig,
            _contentDialogService)]);
    }

    /// <summary>
    /// Окну пора закрыться: по кнопке "Коллизии" открыт навигатор, и работа идёт уже в нём.
    /// </summary>
    public event EventHandler CloseRequested;

    private void SetAllChecks(IEnumerable<CheckViewModel> checks) {
        if(AllChecks is not null) {
            AllChecks.CollectionChanged -= AllChecksCollectionChanged;
            foreach(var check in AllChecks) {
                Unsubscribe(check);
            }
        }

        AllChecks = [..checks];
        AllChecks.CollectionChanged += AllChecksCollectionChanged;
        foreach(var check in AllChecks) {
            Subscribe(check);
        }
        SetChecks(AllChecks);
        RaisePropertyChanged(nameof(AllChecksSelected));
    }

    /// <summary>
    /// Флажок в заголовке таблицы: отмечены все видимые проверки, ни одной или часть
    /// (<see langword="null" />).
    /// </summary>
    /// <remarks>
    /// Действует на проверки, видимые при текущем поиске: "найти HVC и отметить все" должно
    /// отметить проверки HVC, а не скрытые. Щелчок по флажку в промежуточном состоянии отмечает
    /// все - WPF при этом присылает false, поэтому частичное состояние разбирается здесь.
    /// </remarks>
    public bool? AllChecksSelected {
        get {
            var visible = GetVisibleChecks();
            int selected = visible.Count(item => item.IsSelected);
            return selected == 0 ? false : selected == visible.Count ? true : null;
        }
        set {
            bool select = AllChecksSelected is null || value == true;
            foreach(var check in GetVisibleChecks()) {
                check.IsSelected = select;
            }
            RaisePropertyChanged(nameof(AllChecksSelected));
        }
    }

    private List<CheckViewModel> GetVisibleChecks() {
        return Checks?.View?.Cast<CheckViewModel>().ToList() ?? AllChecks?.ToList() ?? [];
    }

    private void AllChecksCollectionChanged(object sender, NotifyCollectionChangedEventArgs e) {
        foreach(var check in e.OldItems?.OfType<CheckViewModel>() ?? []) {
            Unsubscribe(check);
        }
        foreach(var check in e.NewItems?.OfType<CheckViewModel>() ?? []) {
            Subscribe(check);
        }
        RaisePropertyChanged(nameof(AllChecksSelected));
    }

    private void Subscribe(CheckViewModel check) {
        check.PropertyChanged += CheckPropertyChanged;
        check.NavigatorOpened += CheckNavigatorOpened;
    }

    private void Unsubscribe(CheckViewModel check) {
        check.PropertyChanged -= CheckPropertyChanged;
        check.NavigatorOpened -= CheckNavigatorOpened;
    }

    private void CheckNavigatorOpened(object sender, EventArgs e) {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void CheckPropertyChanged(object sender, PropertyChangedEventArgs e) {
        if(e.PropertyName == nameof(CheckViewModel.IsSelected)) {
            RaisePropertyChanged(nameof(AllChecksSelected));
        }
    }

    /// <summary>
    /// Порядок строк - порядок списка: добавленная проверка встаёт в конец, удаление остальных
    /// не трогает. Живой сортировки по имени здесь нет намеренно: имя правится прямо в строке, и
    /// строка уезжала на новое место посреди набора текста, а у проверок с именем по умолчанию
    /// порядок между собой был и вовсе непредсказуем. Отсортировать по имени по-прежнему можно
    /// щелчком по заголовку колонки.
    /// </summary>
    private void SetChecks(ICollection<CheckViewModel> sourceChecks) {
        Checks = new CollectionViewSource() { Source = sourceChecks };
        Checks.Filter += ChecksFilterHandler;
    }

    private void ChecksFilterHandler(object sender, FilterEventArgs e) {
        if(e.Item is CheckViewModel check && !string.IsNullOrWhiteSpace(ChecksFilter)) {
            string str = ChecksFilter.ToLower();
            e.Accepted = check.Name.ToLower().Contains(str)
                || check.FirstSelection.AllFiles.Any(f => f.Name.ToLower().Contains(str))
                || check.FirstSelection.AllProviders.Any(p => p.Name.ToLower().Contains(str))
                || check.SecondSelection.AllFiles.Any(f => f.Name.ToLower().Contains(str))
                || check.SecondSelection.AllProviders.Any(p => p.Name.ToLower().Contains(str));
        }
    }

    private void AddCheck() {
        var check = new CheckViewModel(_revitRepository,
            _localization,
            OpenFileDialogService,
            SaveFileDialogService,
            MessageBoxService,
            _settingsConfig,
            _filtersConfig,
            _contentDialogService);
        AllChecks.Add(check);
        SelectedCheck = check;
    }

    private void RemoveCheck(IList selectedItems) {
        var checks = selectedItems.OfType<CheckViewModel>().ToArray();
        foreach(var check in checks) {
            AllChecks.Remove(check);
        }
        // Выделение указывало на удалённые строки, а по нему считается доступность удаления.
        SelectedCheck = AllChecks.FirstOrDefault();
    }

    private bool CanRemove(IList selectedItems) {
        return selectedItems is not null && AllChecks?.Count > selectedItems.Count;
    }

    /// <remarks>
    /// Исключения ловятся здесь, а не выше: команда вызывается из окна, и всё, что не поймано,
    /// уходит в обработчик WPF и пропадает молча - нажатие OK выглядит так, будто ничего не
    /// произошло. Отмена в окне хода поиска останавливает прогон; сводка показывается по тем
    /// проверкам, что успели пройти до неё - их отчёты уже сохранены.
    /// </remarks>
    /// <summary>
    /// Ставит поиск в очередь Ревита.
    /// </summary>
    /// <remarks>
    /// Окно проверок немодальное, и команда кнопки выполняется вне контекста Revit API: чтение
    /// модели там чаще всего проходит, но это неподдерживаемый путь, который может сломаться на
    /// любой версии. Внешнее событие выполняет поиск в контексте API - как показ коллизии
    /// в навигаторе.
    /// </remarks>
    private void FindClashes() {
        CanCancel = false;
        _revitRepository.DoAction(RunClashSearch);
    }

    private void RunClashSearch() {
        List<CheckSearchResult> results = [];
        string message = _localization.GetLocalizedString("ChecksWindow.SearchCompleted");
        try {
            RenewConfig();
            _checksConfig.SaveProjectConfig();
            RememberSavedState();

            using var progress = new ClashSearchProgress(_localization);
            foreach(var check in AllChecks.Where(item => item.IsSelected)) {
                results.Add(check.SaveClashes(progress));
            }
        } catch(OperationCanceledException) {
            message = _localization.GetLocalizedString("ChecksWindow.SearchCancelled");
        } catch(Exception exception) {
            CanCancel = true;
            MessageText = null;
            Views.MessageWindow.ShowError(
                _localization.GetLocalizedString("ChecksWindow.SearchFailed", exception.Message)
                + Environment.NewLine + Environment.NewLine + exception,
                _localization.GetLocalizedString("ChecksWindow.SummaryTitle"),
                _localization);
            return;
        }

        CanCancel = true;
        MessageText = message;
        ShowSummary(results);
        Wait(() => {
            foreach(var check in AllChecks) {
                check.IsSelected = false;
            }
            MessageText = null;
            CanFindClashes();
        });
    }

    /// <summary>
    /// Показывает, что нашёл каждый прогон. Отдельным окном, а не строкой состояния: проверок
    /// может быть несколько, и по каждой нужна своя строка, а строка состояния к тому же гаснет
    /// через несколько секунд.
    /// </summary>
    private void ShowSummary(IReadOnlyCollection<CheckSearchResult> results) {
        if(results.Count == 0) {
            return;
        }

        string summary = string.Join(Environment.NewLine, results.Select(GetSummaryLine));
        Views.MessageWindow.ShowInfo(
            summary,
            _localization.GetLocalizedString("ChecksWindow.SummaryTitle"),
            _localization);
    }

    private string GetSummaryLine(CheckSearchResult result) {
        if(result.Total == 0) {
            return _localization.GetLocalizedString("ChecksWindow.SummaryEmpty", result.CheckName);
        }
        return result.HadPreviousRun
            ? _localization.GetLocalizedString(
                "ChecksWindow.SummaryLine", result.CheckName, result.Total, result.Added, result.Gone)
            : _localization.GetLocalizedString(
                "ChecksWindow.SummaryFirstRun", result.CheckName, result.Total);
    }

    private void RenewConfig() {
        _checksConfig.Checks = [];
        foreach(var check in AllChecks) {
            _checksConfig.Checks.Add(new Check() {
                Name = check.Name,
                FirstSelection = check.FirstSelection.GetCheckSettings(),
                SecondSelection = check.SecondSelection.GetCheckSettings(),
                Tolerance = check.Tolerance,
                IgnoreRules = check.IgnoreRules
            });
        }
        _checksConfig.RevitVersion = RevitVersionInfo.Version;
    }

    private void SaveConfig() {
        RenewConfig();
        _checksConfig.SaveProjectConfig();
        RememberSavedState();
        MessageText = _localization.GetLocalizedString("ChecksWindow.ChecksSaved");
        Wait(() => { MessageText = null; });
    }

    /// <summary>
    /// Выгружает проверки в XML тестов Clash Detective. Наборы для них выгружаются отдельно,
    /// из окна поисковых наборов: тест ссылается на набор по имени.
    /// </summary>
    private void ExportNavisworks() {
        var checks = AllChecks.Select(check => new Check {
            Name = check.Name,
            FirstSelection = check.FirstSelection.GetCheckSettings(),
            SecondSelection = check.SecondSelection.GetCheckSettings(),
            Tolerance = check.Tolerance
        }).ToList();

        var report = new NavisworksImportReport();
        var document = new NavisworksExporter(_revitRepository.Doc, _localization)
            .ExportClashTests(checks, _revitRepository.GetObjectName(), report);
        string path = new ConfigSaverService(_revitRepository, SaveFileDialogService)
            .SaveXml(document, "ClashTests");

        var summary = new System.Text.StringBuilder();
        summary.AppendLine(_localization.GetLocalizedString(
            "Export.ChecksSummary", checks.Count - report.Skipped.Count, checks.Count, path));
        (string Header, IReadOnlyList<string> Lines)[] sections = [
            ("Import.SkippedHeader", report.Skipped),
            ("Import.NotesHeader", report.Notes)
        ];
        foreach(var (header, lines) in sections) {
            if(lines.Count == 0) {
                continue;
            }
            summary.AppendLine();
            summary.AppendLine(_localization.GetLocalizedString(header, lines.Count));
            foreach(string line in lines) {
                summary.AppendLine("• " + line);
            }
        }
        Views.MessageWindow.ShowInfo(
            summary.ToString().TrimEnd(),
            _localization.GetLocalizedString("Export.Title"),
            _localization);
    }

    private void SaveAsConfig() {
        RenewConfig();
        var s = new ConfigSaverService(_revitRepository, SaveFileDialogService);
        s.Save(_checksConfig);
        MessageText = _localization.GetLocalizedString("ChecksWindow.ChecksSaved");
        Wait(() => { MessageText = null; });

    }

    private void LoadConfig() {
        var cl = new ConfigLoaderService(_revitRepository, _localization, OpenFileDialogService, MessageBoxService);
        var config = cl.Load<ChecksConfig>();
        cl.CheckConfig(config);

        var newChecks = InitializeChecks(config).ToList();
        var nameResolver = new NameResolver<CheckViewModel>(AllChecks, newChecks);
        SetAllChecks(nameResolver.GetCollection().OrderBy(c => c.Name));
        MessageText = _localization.GetLocalizedString("ChecksWindow.ChecksLoaded");
        Wait(() => { MessageText = null; });
    }

    private bool HasSameNames() {
        return AllChecks.Select(item => item.Name)
            .GroupBy(item => item)
            .Any(item => item.Count() > 1);
    }

    private bool CanFindClashes() {
        if(HasSameNames()) {
            ErrorText = _localization.GetLocalizedString("ChecksWindow.Validation.NamesDuplicated");
            return false;
        }

        var selectedChecks = AllChecks.Where(item => item.IsSelected).ToArray();
        if(selectedChecks.Length == 0) {
            ErrorText = _localization.GetLocalizedString("ChecksWindow.Validation.SelectAnyCheck");
            return false;
        }

        var emptyCheck = selectedChecks.FirstOrDefault(item => !item.IsFilterSelected);
        if(emptyCheck != null) {
            ErrorText = _localization.GetLocalizedString(
                "ChecksWindow.Validation.EmptyFilterInCheck", emptyCheck.Name);
            return false;
        }

        emptyCheck = selectedChecks.FirstOrDefault(item => !item.IsFilesSelected);
        if(emptyCheck != null) {
            ErrorText = _localization.GetLocalizedString(
                "ChecksWindow.Validation.EmptyFileInCheck", emptyCheck.Name);
            return false;
        }
        ErrorText = null;
        return true;
    }

    /// <summary>
    /// Вопрос о сохранении при закрытии окна.
    /// </summary>
    /// <remarks>
    /// <c>Views.MessageWindow</c>, а не <c>IMessageBoxService</c> из WPF-UI: та рисовала в этом
    /// окне пустую плашку без текста и кнопок, то есть закрыть было нечем, а висит этот вопрос
    /// на Closing и запирал само окно. Ошибка диалога гашится по той же причине: потерять вопрос
    /// лучше, чем не дать закрыть окно.
    /// </remarks>
    /// <summary>
    /// Состояние проверок, как оно записано в файл проекта: имена, файлы и наборы выборок.
    /// </summary>
    /// <remarks>
    /// По нему решается, спрашивать ли о сохранении при закрытии: раньше вопрос задавался
    /// всегда, даже если в окне ничего не меняли. Отметки "запустить" и найденные коллизии
    /// в состояние не входят - их кнопка "Сохранить" и не сохраняет.
    /// </remarks>
    private string GetChecksState() {
        return JsonConvert.SerializeObject(AllChecks.Select(check => new {
            check.Name,
            check.Tolerance,
            check.IgnoreRules,
            First = check.FirstSelection.GetCheckSettings(),
            Second = check.SecondSelection.GetCheckSettings()
        }));
    }

    private void RememberSavedState() {
        _savedState = GetChecksState();
    }

    private void AskForSave() {
        if(GetChecksState() == _savedState) {
            return;
        }

        try {
            if(Views.MessageWindow.AskYesNo(
                   _localization.GetLocalizedString("ChecksWindow.SavePrompt"),
                   _localization.GetLocalizedString("BIM"),
                   _localization)) {
                SaveClashesCommand.Execute(default);
            }
        } catch(Exception) {
        }
    }

    private void Wait(Action action) {
        var timer = new DispatcherTimer {
            Interval = new TimeSpan(0, 0, 0, 3)
        };
        timer.Tick += (s, a) => { action(); timer.Stop(); };
        timer.Start();
    }
}
