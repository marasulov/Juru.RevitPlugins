using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Input;

using Autodesk.Revit.UI;

using dosymep.Bim4Everyone;
using dosymep.SimpleServices;
using dosymep.WPF.Commands;
using dosymep.WPF.ViewModels;

using Juru.RevitPlugins.ClashNavigator.Models;
using Juru.RevitPlugins.ClashNavigator.Models.Clashes;
using Juru.RevitPlugins.ClashNavigator.Models.RevitClashReport;
using Juru.RevitPlugins.ClashNavigator.Models.RevitClashReport.ServerClashes;
using Juru.RevitPlugins.Core.FileNaming;
using Juru.RevitPlugins.ClashNavigator.Commands;
using Juru.RevitPlugins.ClashNavigator.Services.ClashStatuses;
using Juru.RevitPlugins.ClashNavigator.Services.ReportsMerging;
using Juru.RevitPlugins.ClashNavigator.Services.RevitViewSettings;

using Wpf.Ui;

using IMessageBoxService = dosymep.SimpleServices.IMessageBoxService;
using IOpenFileDialogService = dosymep.SimpleServices.IOpenFileDialogService;
using ISaveFileDialogService = dosymep.SimpleServices.ISaveFileDialogService;

namespace Juru.RevitPlugins.ClashNavigator.ViewModels.Navigator;

internal class NavigatorViewModel : BaseViewModel {
    private readonly RevitRepository _revitRepository;
    private readonly ILocalizationService _localizationService;
    private readonly IContentDialogService _contentDialogService;
    private readonly SettingsConfig _settingsConfig;
    private readonly IClashStatusService _clashStatusService;
    private bool _openFromClashDetector;
    private ReportViewModel _selectedFile;
    private ObservableCollection<ReportViewModel> _reports;
    private ReportsMergeViewModel _reportsMergeViewModel;
    private bool _showReportsMergeView;
    private string _diagnostic;

    public NavigatorViewModel(
        RevitRepository revitRepository,
        IOpenFileDialogService openFileDialogService,
        ISaveFileDialogService saveFileDialogService,
        IMessageBoxService messageBoxService,
        ILocalizationService localizationService,
        IContentDialogService contentDialogService,
        SettingsConfig settingsConfig,
        string reportName = null) {

        _revitRepository = revitRepository ?? throw new ArgumentNullException(nameof(revitRepository));
        OpenFileDialogService = openFileDialogService ?? throw new ArgumentNullException(nameof(openFileDialogService));
        SaveFileDialogService = saveFileDialogService ?? throw new ArgumentNullException(nameof(saveFileDialogService));
        MessageBoxService = messageBoxService ?? throw new ArgumentNullException(nameof(messageBoxService));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _contentDialogService = contentDialogService ?? throw new ArgumentNullException(nameof(contentDialogService));
        _settingsConfig = settingsConfig ?? throw new ArgumentNullException(nameof(settingsConfig));
        _clashStatusService = new ServerClashStatusService(_settingsConfig);
        Title = BuildTitle();
        Reports = [];

        if(reportName == null) {
            InitializeReports();
        } else {
            InitializeReports(reportName);
        }

        OpenClashDetectorCommand = RelayCommand.Create(OpenClashDetector, CanOpenClashDetector);
        LoadCommand = RelayCommand.Create(Load);
        LoadFromServerCommand = RelayCommand.Create(LoadFromServer);
        LoadFromServerOnStartupCommand = RelayCommand.Create(LoadFromServerOnStartup);
        DeleteCommand = RelayCommand.Create(Delete, CanDelete);
        // Без проверки CanExecute: у кнопки в шаблоне ячейки CommandParameter привязывается позже
        // самой команды, CanExecute успевает отработать с null и выключает кнопку навсегда -
        // доступность кнопки задаёт привязка IsEnabled к IsResolved, а сам метод проверяет коллизию.
        SelectClashCommand = RelayCommand.Create<IClashViewModel>(SelectClash);
        EditCommentsCommand = RelayCommand.Create<ICommentable>(ShowCommentsEditor, CanEditComments);
        ShowCommentsCommand = RelayCommand.Create<ICommentable>(ShowComments, CanEditComments);
        AcceptReportsMergeCommand =
            RelayCommand.Create<ReportsMergeViewModel>(AcceptReportsMerge, CanAcceptReportsMerge);
        CancelReportsMergeCommand = RelayCommand.Create(CancelReportsMerge);
        SaveAllReportsCommand = RelayCommand.Create(SaveAllReports, CanSaveAllReports);
        // Кнопка активна при любом открытом отчёте, даже когда отправлять нечего: иначе объяснение,
        // почему статусы не уходят (отчёт не с сервера), пользователю негде получить.
        SendStatusesCommand = RelayCommand.Create(SendStatuses, () => Reports.Count > 0);
        AskForSaveCommand = RelayCommand.Create(AskForSave);
    }


    public ICommand SelectClashCommand { get; }
    public ICommand OpenClashDetectorCommand { get; }
    public ICommand LoadCommand { get; }
    public ICommand LoadFromServerCommand { get; }
    public ICommand LoadFromServerOnStartupCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand SaveAllReportsCommand { get; }
    public ICommand SendStatusesCommand { get; }
    public ICommand AskForSaveCommand { get; }
    public ICommand EditCommentsCommand { get; }
    public ICommand ShowCommentsCommand { get; }
    public ICommand AcceptReportsMergeCommand { get; }
    public ICommand CancelReportsMergeCommand { get; }
    public IOpenFileDialogService OpenFileDialogService { get; }
    public ISaveFileDialogService SaveFileDialogService { get; }
    public IMessageBoxService MessageBoxService { get; }

    public ReportsMergeViewModel ReportsMergeViewModel {
        get => _reportsMergeViewModel;
        set => RaiseAndSetIfChanged(ref _reportsMergeViewModel, value);
    }

    public bool ShowReportsMergeView {
        get => _showReportsMergeView;
        set => RaiseAndSetIfChanged(ref _showReportsMergeView, value);
    }

    public ObservableCollection<ReportViewModel> Reports {
        get => _reports;
        set => RaiseAndSetIfChanged(ref _reports, value);
    }

    /// <summary>
    /// Заголовок окна со временем сборки загруженной сборки плагина.
    /// Ревит не умеет подменять уже загруженную сборку, поэтому по этой дате видно,
    /// работает ли пересобранный код или процесс держит прежний.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Надпись кнопки показа коллизии и подсказка списка статусов.
    /// </summary>
    /// <remarks>
    /// Строки берутся здесь, а не разметкой из словаря: обе живут в шаблоне ячейки таблицы,
    /// а туда расширение <c>me:LocalizationSource</c> попадает невычисленным объектом
    /// и показывает вместо текста своё имя типа.
    /// </remarks>
    public string ShowTitle => _localizationService.GetLocalizedString("Navigator.Show");

    /// <inheritdoc cref="ShowTitle" />
    public string StatusTooltip => _localizationService.GetLocalizedString("Navigator.StatusTooltip");

    /// <summary>
    /// Сколько изменений статусов ждёт отправки в базу по всем отчётам.
    /// </summary>
    public int UnsentStatusesCount => Reports?.Sum(report => report.UnsentStatusesCount) ?? 0;

    /// <summary>
    /// Надпись на кнопке отправки статусов: количество на кнопке отвечает на вопрос
    /// "когда это уйдёт на сервер" без чтения строки состояния.
    /// </summary>
    public string SendStatusesTitle => UnsentStatusesCount == 0
        ? _localizationService.GetLocalizedString("Navigator.SendStatuses")
        : _localizationService.GetLocalizedString("Navigator.SendStatusesCount", UnsentStatusesCount);

    public string Diagnostic {
        get => _diagnostic;
        private set => RaiseAndSetIfChanged(ref _diagnostic, value);
    }

    public bool OpenFromClashDetector {
        get => _openFromClashDetector;
        set => RaiseAndSetIfChanged(ref _openFromClashDetector, value);
    }

    public ReportViewModel SelectedReport {
        get => _selectedFile;
        set => RaiseAndSetIfChanged(ref _selectedFile, value);
    }

    /// <summary>
    /// Загружать отчёт с сервера коллизий сразу при открытии навигатора.
    /// </summary>
    public bool AutoLoadFromServer {
        get => _settingsConfig.AutoLoadFromServer;
        set {
            if(_settingsConfig.AutoLoadFromServer != value) {
                _settingsConfig.AutoLoadFromServer = value;
                _settingsConfig.SaveProjectConfig();
                RaisePropertyChanged(nameof(AutoLoadFromServer));
            }
        }
    }

    /// <summary>
    /// Возвращает сохранённые настройки колонок таблицы коллизий, ключ - заголовок колонки.
    /// </summary>
    public IReadOnlyDictionary<string, ClashColumnSettings> GetColumnSettings() {
        return _settingsConfig.NavigatorColumns ?? [];
    }

    /// <summary>
    /// Сохраняет настройки колонок таблицы коллизий, чтобы восстановить их при следующем открытии окна.
    /// </summary>
    /// <param name="columns">Настройки колонок, ключ - заголовок колонки</param>
    public void SaveColumnSettings(IReadOnlyDictionary<string, ClashColumnSettings> columns) {
        if(columns is null) { throw new ArgumentNullException(nameof(columns)); }

        _settingsConfig.NavigatorColumns = columns.ToDictionary(item => item.Key, item => item.Value);
        _settingsConfig.SaveProjectConfig();
    }

    private string BuildTitle() {
        try {
            string location = Assembly.GetExecutingAssembly().Location;
            return _localizationService.GetLocalizedString(
                "Navigator.TitleWithBuild", File.GetLastWriteTime(location).ToString("dd.MM.yyyy HH:mm"));
        } catch(Exception) {
            return _localizationService.GetLocalizedString("Navigator.Title");
        }
    }

    private void InitializeReports(string reportName = null) {
        string reportsDir = Path.Combine(
            RevitRepository.LocalProfilePath,
            RevitVersionInfo.Version,
            nameof(Juru.RevitPlugins.ClashNavigator),
            _revitRepository.GetObjectName());

        if(!Directory.Exists(reportsDir)) {
            return;
        }

        ICollection<ReportViewModel> reports;
        if(reportName != null) {
            reports = GetReports(Path.Combine(reportsDir, reportName + ".json"));
        } else {
            reports = Directory.GetFiles(reportsDir, "*.json")
                .SelectMany(GetReports)
                .ToArray();
        }

        SetReports(reports);
    }

    private void OpenClashDetector() {
        _revitRepository.DoAction(() => {
            //var command = new DetectiveClashesCommand();
            //command.ExecuteCommand(_revitRepository.UiApplication);
        });
    }

    private bool CanOpenClashDetector() {
        return OpenFromClashDetector;
    }

    /// <summary>
    /// Отправляет в базу статусы, изменённые во всех отчётах, и всегда сообщает результат диалогом:
    /// отправка по кнопке - осознанное действие пользователя, молчать в ответ нельзя.
    /// </summary>
    private void SendStatuses() {
        var messages = new List<string>();
        bool hasError = false;
        foreach(var report in Reports) {
            (string? message, bool isError) = report.SendStatuses();
            if(message is not null) {
                messages.Add(Reports.Count > 1 ? $"{report.Name}: {message}" : message);
            }
            hasError |= isError;
        }

        if(messages.Count == 0) {
            // Разбираем по отчётам, а не общей фразой: в списке одновременно бывают отчёт с сервера
            // и отчёт из файла, и "изменений нет" про весь список вводило в заблуждение.
            var localReports = Reports.Where(report => !report.IsFromServer).Select(report => report.Name).ToArray();
            messages.Add(localReports.Length == 0
                ? _localizationService.GetLocalizedString("Navigator.NoStatusChanges")
                : _localizationService.GetLocalizedString("Navigator.NothingToSend",
                    string.Join(Environment.NewLine, localReports.Select(name => $"— {name}"))));
        }

        string text = string.Join(Environment.NewLine, messages);
        Diagnostic = text.Replace(Environment.NewLine, " ");
        RaiseUnsentStatusesChanged();

        MessageBox.Show(text, _localizationService.GetLocalizedString("Navigator.SendStatusesTitle"),
            MessageBoxButton.OK,
            hasError ? MessageBoxImage.Error : MessageBoxImage.Information);
    }

    /// <summary>
    /// Есть изменения статусов, не отправленные на сервер коллизий.
    /// </summary>
    private bool HasUnsentStatuses => UnsentStatusesCount > 0;

    private void AskForSave() {
        // Спрашиваем только когда есть что терять: без изменений статусов диалог при закрытии мешает.
        if(!HasUnsentStatuses) {
            return;
        }

        // Текст задан здесь: файла локализации в плагине нет, и GetLocalizedString возвращала
        // пустую строку - при закрытии окна выходил диалог без вопроса.
        var answer = MessageBoxService.Show(
            _localizationService.GetLocalizedString("Navigator.AskForSave", UnsentStatusesCount),
            _localizationService.GetLocalizedString("Navigator.Title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if(answer == MessageBoxResult.Yes) {
            SendStatuses();
            SaveAllReports();
        }
    }

    /// <summary>
    /// Загружает отчеты из заданного файла.
    /// </summary>
    /// <param name="reportPath">Путь к файлу отчета. Например, xml из нэвиса или json из ревита.</param>
    /// <returns>Коллекция отчетов из заданного файла.</returns>
    private IList<ReportViewModel> GetReports(string reportPath) {
        return ReportLoader.GetReports(_revitRepository, reportPath)
            .Select(r => new ReportViewModel(
                _revitRepository,
                r.Name,
                _localizationService,
                OpenFileDialogService,
                SaveFileDialogService,
                MessageBoxService,
                _contentDialogService,
                _settingsConfig,
                _clashStatusService,
                r.Clashes?.ToArray() ?? []))
            .ToArray();
    }

    private void Load() {
        try {
            LoadReportFile();
        } catch(OperationCanceledException) {
            Diagnostic = _localizationService.GetLocalizedString("Navigator.LoadCancelled");
        } catch(Exception exception) {
            Diagnostic = _localizationService.GetLocalizedString(
                "Navigator.ImportError", exception.GetBaseException().Message);
            ShowErrorDialog(Diagnostic);
        }
    }

    private void LoadReportFile() {
        string path = _revitRepository.GetFileDialogPath();
        if(!OpenFileDialogService.ShowDialog(path)) throw new OperationCanceledException();

        var existingReportsSet = new ReportSet(Reports);
        var importingReports = GetReports(OpenFileDialogService.File.FullName);
        var importingReportsSet = new ReportSet(importingReports);
        var reportsIntersection = existingReportsSet.Intersect(importingReportsSet);
        var namesResolver = new ReportsIntersectionResolver();
        if(reportsIntersection.HasIntersection) {
            switch(GetLoadMode(string.Join(", ", reportsIntersection.LeftInnerSet.Reports.Select(r => r.Name)))) {
                case TaskDialogResult.CommandLink1: {
                    StartReportsMerge(reportsIntersection);
                    break;
                }
                case TaskDialogResult.CommandLink2: {
                    SetReports(namesResolver.Replace(reportsIntersection));
                    break;
                }
                case TaskDialogResult.CommandLink3: {
                    SetReports(namesResolver.CopyAndRename(reportsIntersection));
                    break;
                }
                default: {
                    // отмена
                    return;
                }
            }
        } else {
            SetReports(Reports.Union(importingReports));
        }

        _revitRepository.CommonConfig.LastRunPath = OpenFileDialogService.File.DirectoryName;
        _revitRepository.CommonConfig.SaveProjectConfig();

        int imported = Reports.Sum(report => report.ImportedClashesCount);
        int resolved = Reports.Sum(report => report.ResolvedClashesCount);
        Diagnostic = _localizationService.GetLocalizedString(
            "Navigator.LoadSummary", Reports.Count, imported, resolved);
        WarnAboutUnloadedLinks();
    }

    /// <summary>
    /// Загружает отчёт с сервера при открытии окна, если это включено пользователем.
    /// В отличие от загрузки по кнопке, ошибки не показываются диалогом: открытие навигатора
    /// не должно упираться в недоступный сервер, о проблеме сообщает строка состояния.
    /// </summary>
    private void LoadFromServerOnStartup() {
        // При открытии из ClashDetective навигатор показывает конкретный отчёт проверки,
        // подмешивать к нему серверный отчёт не нужно.
        if(!AutoLoadFromServer || OpenFromClashDetector) {
            return;
        }

        LoadFromServer(silent: true);
    }

    private void LoadFromServer() {
        // Включаем автозагрузку после первой успешной загрузки, чтобы не нажимать кнопку каждый раз.
        if(LoadFromServer(silent: false)) {
            AutoLoadFromServer = true;
        }
    }

    /// <returns>True, если отчёт загружен с сервера.</returns>
    private bool LoadFromServer(bool silent) {
        try {
            string fileName = _revitRepository.GetDocumentName();
            if(!RevitFileNameInfo.TryParse(fileName, out var fileInfo)) {
                Diagnostic = _localizationService.GetLocalizedString(
                    "Navigator.FileNameNotStandard", fileName);
                ShowError(Diagnostic, silent);
                return false;
            }

            // Настройка пользователя переопределяет адрес из ClashServer.json рядом со сборкой.
            string serverUrl = ClashServerOptions.ResolveUrl(_settingsConfig);
            if(string.IsNullOrWhiteSpace(serverUrl)) {
                Diagnostic = _localizationService.GetLocalizedString("Navigator.NoServerUrl");
                ShowError(Diagnostic, silent);
                return false;
            }

            var client = new ClashServerClient(serverUrl);
            var report = ServerReportLoader.GetReportAsync(client, fileInfo, _revitRepository)
                .ConfigureAwait(false).GetAwaiter().GetResult();

            var reportViewModel = new ReportViewModel(
                _revitRepository,
                report.Name,
                _localizationService,
                OpenFileDialogService,
                SaveFileDialogService,
                MessageBoxService,
                _contentDialogService,
                _settingsConfig,
                _clashStatusService,
                KeepUnsentStatuses(report));

            // Выбираем именно загруженный отчёт: он относится к открытому файлу.
            SetReports(
                Reports.Where(r => r.Name != reportViewModel.Name).Append(reportViewModel),
                reportViewModel);

            // Адрес обязательно называем: настройка пользователя перекрывает адрес из ClashServer.json,
            // и разбор "почему статусы не те" однажды свёлся к тому, что плагин молча читал
            // другой сервер. Из строки состояния это видно сразу.
            Diagnostic = _localizationService.GetLocalizedString(
                "Navigator.LoadedFromServer", reportViewModel.ImportedClashesCount, client.BaseUrl);
            if(!silent) {
                WarnAboutUnloadedLinks();
            }
            return true;
        } catch(Exception exception) {
            // Адрес берём заново: переменная из try в catch не видна, а знать, к какому серверу
            // не удалось обратиться, важнее всего именно в сообщении об ошибке.
            Diagnostic = _localizationService.GetLocalizedString(
                "Navigator.ServerLoadError",
                ClashServerOptions.ResolveUrl(_settingsConfig),
                exception.GetBaseException().Message);
            ShowError(Diagnostic, silent);
            return false;
        }
    }

    /// <summary>
    /// Переносит в загруженный с сервера отчёт статусы, которые пользователь изменил, но не отправил.
    /// Без этого загрузка с сервера (в том числе автоматическая при открытии окна) молча
    /// затирала правки статусов: они пропадали из таблицы вместе с прежним отчётом.
    /// </summary>
    /// <param name="report">Отчёт, загруженный с сервера</param>
    /// <returns>Коллизии отчёта с восстановленными неотправленными статусами</returns>
    private ICollection<ClashModel> KeepUnsentStatuses(ReportModel report) {
        var clashes = report.Clashes?.ToArray() ?? [];
        var previous = Reports.FirstOrDefault(item => item.Name == report.Name);
        if(previous is null) {
            return clashes;
        }

        // Ключ - Id коллизии в базе: только по нему прежний и новый отчёты сопоставимы.
        var unsentStatuses = previous.GetClashes()
            .Where(clash => clash.HasUnsentStatus)
            .GroupBy(clash => clash.Clash.ServerId!.Value)
            .ToDictionary(group => group.Key, group => group.First().ClashStatus);
        if(unsentStatuses.Count == 0) {
            return clashes;
        }

        int restored = 0;
        foreach(var clash in clashes) {
            if(clash.ServerId is not null
                && unsentStatuses.TryGetValue(clash.ServerId.Value, out var status)) {
                // ServerStatus остаётся серверным, поэтому статус так и числится неотправленным.
                clash.ClashStatus = status;
                restored++;
            }
        }

        if(restored > 0) {
            Diagnostic = _localizationService.GetLocalizedString("Navigator.UnsentStatusesKept", restored);
        }
        return clashes;
    }

    private void ShowError(string message, bool silent) {
        if(silent) {
            return;
        }

        ShowErrorDialog(message);
    }

    /// <summary>
    /// Показывает ошибку диалогом под общим для плагина заголовком.
    /// </summary>
    private void ShowErrorDialog(string message) {
        MessageBox.Show(message, _localizationService.GetLocalizedString("Navigator.Title"),
            MessageBoxButton.OK, MessageBoxImage.Error);
    }

    /// <summary>
    /// Сообщает о выгруженных связях. Элементы таких связей не открываются из отчёта:
    /// репозиторий видит только загруженные документы.
    /// </summary>
    private void WarnAboutUnloadedLinks() {
        var unloadedLinks = _revitRepository.GetUnloadedLinkNames();
        if(unloadedLinks.Count == 0) {
            return;
        }

        string names = string.Join(Environment.NewLine, unloadedLinks.Select(name => $"— {name}"));
        MessageBoxService.Show(
            _localizationService.GetLocalizedString("Navigator.UnloadedLinks", names),
            _localizationService.GetLocalizedString("Navigator.Title"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    /// <param name="selected">
    /// Отчёт, который нужно выбрать. Если не задан, выбирается первый по алфавиту.
    /// </param>
    private void SetReports(IEnumerable<ReportViewModel> reports, ReportViewModel selected = null) {
        foreach(var report in Reports) {
            report.PropertyChanged -= OnReportPropertyChanged;
        }

        Reports = new ObservableCollection<ReportViewModel>(reports.OrderBy(r => r.Name));
        // Счётчик на кнодке отправки должен считать изменения во всех отчётах, а не только в выбранном.
        foreach(var report in Reports) {
            report.PropertyChanged += OnReportPropertyChanged;
        }

        SelectedReport = null;
        // Первый по алфавиту - не то же самое, что нужный: в списке лежат сохранённые отчёты
        // других дисциплин, и после загрузки с сервера выбранным оказывался отчёт чужого файла,
        // где ни один элемент не находится в открытом документе.
        SelectedReport = selected is not null && Reports.Contains(selected)
            ? selected
            : Reports.FirstOrDefault();
        RaiseUnsentStatusesChanged();
    }

    private void OnReportPropertyChanged(object sender, PropertyChangedEventArgs e) {
        if(e.PropertyName == nameof(ReportViewModel.UnsentStatusesCount)) {
            RaiseUnsentStatusesChanged();
        }
    }

    private void RaiseUnsentStatusesChanged() {
        RaisePropertyChanged(nameof(UnsentStatusesCount));
        RaisePropertyChanged(nameof(SendStatusesTitle));
    }

    private void Delete() {
        if(MessageBoxService.Show(
            _localizationService.GetLocalizedString("Navigator.DeletePrompt"),
            _localizationService.GetLocalizedString("BIM"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.Cancel) == MessageBoxResult.Yes) {
            DeleteConfig(SelectedReport.GetUpdatedConfig());
            Reports.Remove(SelectedReport);
            SelectedReport = Reports.FirstOrDefault();
        }
    }

    private void DeleteConfig(ClashesConfig config) {
        if(File.Exists(config.ProjectConfigPath) && config.ProjectConfigPath.EndsWith(".json", StringComparison.CurrentCultureIgnoreCase)) {
            File.Delete(config.ProjectConfigPath);
        }
    }

    private bool CanDelete() {
        return SelectedReport != null;
    }

    /// <summary>
    /// Снимает цветную подсветку коллизии - вызывается при закрытии окна навигатора.
    /// </summary>
    public void ClearHighlight() {
        _revitRepository.QueueClearHighlight();
    }

    private void SelectClash(IClashViewModel clash) {
        if(clash is not ClashViewModel resolvable || !resolvable.IsResolved) {
            // Показывать нечего: элементы коллизии не найдены в открытом документе и связях.
            // Причина видна в подсказке кнопки, диалогом не мешаем.
            return;
        }

        var settings = new ClashViewSettings(_revitRepository, _localizationService, clash, _settingsConfig);
        var elements = clash.GetElements();
        _revitRepository.QueueSelectAndShowElement(elements, settings, _settingsConfig.HighlightClashElements);
    }

    private void SaveAllReports() {
        // Кнопка обязана давать видимый результат: без этой сводки нажатие выглядело как
        // "ничего не произошло", когда отправлять на сервер было нечего.
        try {
            int unsent = Reports.Sum(report => report.UnsentStatusesCount);
            foreach(var report in Reports) {
                report.SaveCommand.Execute(default);
            }

            // Сохранение только пишет файлы: в базу статусы уходят кнопкой "Отправить статусы".
            Diagnostic = unsent == 0
                ? _localizationService.GetLocalizedString("Navigator.ReportsSaved", Reports.Count)
                : _localizationService.GetLocalizedString(
                    "Navigator.ReportsSavedUnsent", Reports.Count, unsent);
        } catch(Exception exception) {
            Diagnostic = _localizationService.GetLocalizedString(
                "Navigator.SaveError", exception.GetBaseException().Message);
            ShowErrorDialog(Diagnostic);
        }
    }

    private bool CanSaveAllReports() {
        return Reports.Count > 0;
    }

    private void ShowCommentsEditor(ICommentable commentable) {
        // The imported project used DevExpress's dialog service, which is not a
        // dependency of this plugin. Keep comments available without coupling
        // the navigator to that UI framework.
        string comments = string.Join(Environment.NewLine, commentable.Comments
            .Select(c => $"{c.Author}: {c.Body}"));
        MessageBox.Show(comments, _localizationService.GetLocalizedString(
            "Navigator.ClashComments", commentable.CommentsTitle), MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private bool CanEditComments(ICommentable clash) {
        return clash is not null;
    }

    private void ShowComments(ICommentable commentable) {
        bool backup = commentable.CanEditComments;
        commentable.CanEditComments = false;
        ShowCommentsEditor(commentable);
        commentable.CanEditComments = backup;
    }

    private TaskDialogResult GetLoadMode(string names) {
        var dialog = new TaskDialog(_localizationService.GetLocalizedString("ReportsResolver.Header")) {
            MainContent = _localizationService.GetLocalizedString("ReportsResolver.Body", names)
        };
        dialog.AddCommandLink(
            TaskDialogCommandLinkId.CommandLink1,
            _localizationService.GetLocalizedString("ReportsResolver.Merge"));
        dialog.AddCommandLink(
            TaskDialogCommandLinkId.CommandLink2,
            _localizationService.GetLocalizedString("ReportsResolver.Replace"));
        dialog.AddCommandLink(
            TaskDialogCommandLinkId.CommandLink3,
            _localizationService.GetLocalizedString("ReportsResolver.CopyAndRename"));
        dialog.CommonButtons = TaskDialogCommonButtons.Cancel;
        return dialog.Show();
    }

    private void StartReportsMerge(ReportSetsIntersectionResult reportsIntersection) {
        ReportsMergeViewModel = new ReportsMergeViewModel(_revitRepository, _localizationService, reportsIntersection);
        if(ReportsMergeViewModel.VisibleReports.All(r => r.Items.Count == 0)) {
            AcceptReportsMergeCommand.Execute(ReportsMergeViewModel);
            return;
        }
        ShowReportsMergeView = true;
    }

    private void AcceptReportsMerge(ReportsMergeViewModel vm) {
        SetReports(vm.GetMergeResult());
        ShowReportsMergeView = false;
        ReportsMergeViewModel = null;
    }

    private bool CanAcceptReportsMerge(ReportsMergeViewModel vm) {
        if(vm is null) {
            return false;
        }

        return vm.AcceptMergeCommand.CanExecute(null);
    }

    private void CancelReportsMerge() {
        ShowReportsMergeView = false;
        ReportsMergeViewModel = null;
    }
}
