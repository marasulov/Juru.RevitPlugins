using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
using Juru.RevitPlugins.ClashNavigator.Commands;
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
        Reports = [];

        if(reportName == null) {
            InitializeReports();
        } else {
            InitializeReports(reportName);
        }

        OpenClashDetectorCommand = RelayCommand.Create(OpenClashDetector, CanOpenClashDetector);
        LoadCommand = RelayCommand.Create(Load);
        LoadFromServerCommand = RelayCommand.Create(LoadFromServer);
        DeleteCommand = RelayCommand.Create(Delete, CanDelete);
        SelectClashCommand = RelayCommand.Create<IClashViewModel>(SelectClash, CanSelectClash);
        EditCommentsCommand = RelayCommand.Create<ICommentable>(ShowCommentsEditor, CanEditComments);
        ShowCommentsCommand = RelayCommand.Create<ICommentable>(ShowComments, CanEditComments);
        AcceptReportsMergeCommand =
            RelayCommand.Create<ReportsMergeViewModel>(AcceptReportsMerge, CanAcceptReportsMerge);
        CancelReportsMergeCommand = RelayCommand.Create(CancelReportsMerge);
        SaveAllReportsCommand = RelayCommand.Create(SaveAllReports, CanSaveAllReports);
        AskForSaveCommand = RelayCommand.Create(AskForSave);
    }


    public ICommand SelectClashCommand { get; }
    public ICommand OpenClashDetectorCommand { get; }
    public ICommand LoadCommand { get; }
    public ICommand LoadFromServerCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand SaveAllReportsCommand { get; }
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

    private void AskForSave() {
        if(MessageBoxService.Show(_localizationService.GetLocalizedString("Navigator.SavePrompt"),
            _localizationService.GetLocalizedString("BIM"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes) {
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
                r.Clashes?.ToArray() ?? []))
            .ToArray();
    }

    private void Load() {
        try {
            LoadReportFile();
        } catch(OperationCanceledException) {
            Diagnostic = "Загрузка отменена.";
        } catch(Exception exception) {
            Diagnostic = $"Ошибка импорта: {exception.GetBaseException().Message}";
            MessageBox.Show(Diagnostic, "Навигатор коллизий", MessageBoxButton.OK, MessageBoxImage.Error);
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
        Diagnostic = $"Загружено отчётов: {Reports.Count}; коллизий: {imported}; найдено пар в Revit: {resolved}.";
        WarnAboutUnloadedLinks();
    }

    private void LoadFromServer() {
        try {
            string fileName = _revitRepository.GetDocumentName();
            if(!RevitFileNameInfo.TryParse(fileName, out var fileInfo)) {
                Diagnostic = $"Имя файла \"{fileName}\" не соответствует стандарту наименования " +
                    "(Проект-Объект-Дисциплина-vXX).";
                MessageBox.Show(Diagnostic, "Навигатор коллизий", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Настройка пользователя переопределяет адрес из ClashServer.json рядом со сборкой.
            string serverUrl = string.IsNullOrWhiteSpace(_settingsConfig.ClashServerUrl)
                ? ClashServerOptions.DefaultUrl
                : _settingsConfig.ClashServerUrl;
            if(string.IsNullOrWhiteSpace(serverUrl)) {
                Diagnostic = "Не задан адрес сервера коллизий: ни в настройках плагина, ни в файле ClashServer.json.";
                MessageBox.Show(Diagnostic, "Навигатор коллизий", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
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
                report.Clashes?.ToArray() ?? []);

            SetReports(Reports.Where(r => r.Name != reportViewModel.Name).Append(reportViewModel));

            Diagnostic = $"Загружено с сервера коллизий: {reportViewModel.ImportedClashesCount}.";
            WarnAboutUnloadedLinks();
        } catch(Exception exception) {
            Diagnostic = $"Ошибка загрузки с сервера: {exception.GetBaseException().Message}";
            MessageBox.Show(Diagnostic, "Навигатор коллизий", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
            $"Не загружены связи:{Environment.NewLine}{names}{Environment.NewLine}{Environment.NewLine}"
            + "Коллизии с элементами этих связей открыть не получится. "
            + "Загрузите связи через «Диспетчер связей» и включите их видимость на виде.",
            "Навигатор коллизий",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void SetReports(IEnumerable<ReportViewModel> reports) {
        Reports = new ObservableCollection<ReportViewModel>(reports.OrderBy(r => r.Name));
        SelectedReport = null;
        SelectedReport = Reports.FirstOrDefault();
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

    private void SelectClash(IClashViewModel clash) {
        var settings = new ClashViewSettings(_revitRepository, _localizationService, clash, _settingsConfig);
        var elements = clash.GetElements();
        _revitRepository.QueueSelectAndShowElement(elements, settings);
    }

    private bool CanSelectClash(IClashViewModel p) {
        return p is ClashViewModel clash && clash.IsResolved;
    }

    private void SaveAllReports() {
        foreach(var report in Reports) {
            report.SaveCommand.Execute(default);
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
