using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Input;

using dosymep.SimpleServices;
using dosymep.WPF.Commands;
using dosymep.WPF.ViewModels;

using Juru.RevitPlugins.ClashNavigator.Models;
using Juru.RevitPlugins.ClashNavigator.Models.ClashDetection;
using Juru.RevitPlugins.ClashNavigator.Models.Clashes;
using Juru.RevitPlugins.ClashNavigator.Models.FilterModel;
using Juru.RevitPlugins.ClashNavigator.Models.Interfaces;
using Juru.RevitPlugins.ClashNavigator.Services;
using Juru.RevitPlugins.ClashNavigator.ViewModels.Navigator;
using Juru.RevitPlugins.ClashNavigator.Views;
using Juru.RevitPlugins.ClashNavigator.Views.Navigator;

using Wpf.Ui;

namespace Juru.RevitPlugins.ClashNavigator.ViewModels.ClashDetective;
internal class CheckViewModel : BaseViewModel, INamedEntity {
    private readonly RevitRepository _revitRepository;
    private readonly ILocalizationService _localizationService;
    private readonly SettingsConfig _settingsConfig;
    private readonly FiltersConfig _filtersConfig;
    private readonly IContentDialogService _contentDialogService;
    private string _name;
    private double _tolerance;
    private string _errorText;
    private int? _clashCount;
    private bool _isSelected;
    private SelectionViewModel _firstSelection;
    private SelectionViewModel _secondSelection;

    public CheckViewModel(RevitRepository revitRepository,
        ILocalizationService localizationService,
        IOpenFileDialogService openFileDialogService,
        ISaveFileDialogService saveFileDialogService,
        IMessageBoxService messageBoxService,
        SettingsConfig settingsConfig,
        FiltersConfig filtersConfig,
        IContentDialogService contentDialogService,
        Check check = null) {

        _revitRepository = revitRepository ?? throw new ArgumentNullException(nameof(revitRepository));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        OpenFileDialogService = openFileDialogService ?? throw new ArgumentNullException(nameof(openFileDialogService));
        SaveFileDialogService = saveFileDialogService ?? throw new ArgumentNullException(nameof(saveFileDialogService));
        MessageBoxService = messageBoxService ?? throw new ArgumentNullException(nameof(messageBoxService));
        _settingsConfig = settingsConfig ?? throw new ArgumentNullException(nameof(settingsConfig));
        _filtersConfig = filtersConfig ?? throw new ArgumentNullException(nameof(filtersConfig));
        _contentDialogService = contentDialogService ?? throw new ArgumentNullException(nameof(contentDialogService));
        Name = check?.Name ?? _localizationService.GetLocalizedString("ChecksWindow.DefaultCheckName");
        Tolerance = check?.Tolerance ?? 0;

        if(check == null) {
            InitializeSelections();
        } else {
            InitializeFilterProviders(check);
        }

        ShowClashesCommand = new RelayCommand(ShowClashes, CanShowClashes);
    }

    public ICommand SelectMainProviderCommand { get; }
    public ICommand ShowClashesCommand { get; }

    /// <summary>
    /// Навигатор по отчёту проверки открыт - окно проверок может закрыться.
    /// </summary>
    public event EventHandler NavigatorOpened;
    public bool IsFilterSelected => FirstSelection.SelectedProviders.Any() && SecondSelection.SelectedProviders.Any();
    public bool IsFilesSelected => FirstSelection.SelectedFiles.Any() && SecondSelection.SelectedFiles.Any();

    public bool IsSelected {
        get => _isSelected;
        set => RaiseAndSetIfChanged(ref _isSelected, value);
    }

    /// <summary>
    /// Сколько коллизий в отчёте проверки; <see langword="null" />, пока поиск не запускали.
    /// </summary>
    public int? ClashCount {
        get => _clashCount;
        private set {
            RaiseAndSetIfChanged(ref _clashCount, value);
            RaisePropertyChanged(nameof(HasReport));
            // Кнопка "Коллизии" включается по CanExecute, а WPF кеширует его до явного запроса.
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>
    /// Есть что открыть в навигаторе: отчёт с хотя бы одной коллизией.
    /// </summary>
    public bool HasReport => ClashCount > 0;

    public string Name {
        get => _name;
        set => RaiseAndSetIfChanged(ref _name, value);
    }

    public string ErrorText {
        get => _errorText;
        set => RaiseAndSetIfChanged(ref _errorText, value);
    }

    /// <summary>
    /// Допуск, мм: пересечения мельче не считаются коллизиями.
    /// </summary>
    public double Tolerance {
        get => _tolerance;
        set => RaiseAndSetIfChanged(ref _tolerance, Math.Max(0, value));
    }

    public SelectionViewModel FirstSelection {
        get => _firstSelection;
        set => RaiseAndSetIfChanged(ref _firstSelection, value);
    }

    public SelectionViewModel SecondSelection {
        get => _secondSelection;
        set => RaiseAndSetIfChanged(ref _secondSelection, value);
    }

    public string ReportName => $"{_revitRepository.GetDocumentName()}_{Name}";

    public IOpenFileDialogService OpenFileDialogService { get; }
    public ISaveFileDialogService SaveFileDialogService { get; }
    public IMessageBoxService MessageBoxService { get; }

    /// <summary>
    /// Возвращает провайдеры для поиска коллизий. MainProviders - выборка 1 (слева), OtherProviders - выборка 2 (справа).
    /// </summary>
    /// <returns>Провайдер выборки 1 (слева) и провайдер выборки 2 (справа)</returns>
    public (List<IProvider> MainProviders, List<IProvider> OtherProviders) GetProviders() {
        List<IProvider> mainProviders;
        List<IProvider> otherProviders;
        if(FirstSelection.SelectedFiles.Any(item => item.Name.Equals(_revitRepository.GetDocumentName()))) {
            mainProviders = FirstSelection
                .GetProviders()
                .ToList();
            otherProviders = SecondSelection
                .GetProviders()
                .ToList();
        } else {
            mainProviders = SecondSelection
                .GetProviders()
                .ToList();
            otherProviders = FirstSelection
                .GetProviders()
                .ToList();
        }
        return (mainProviders, otherProviders);
    }

    public List<ClashModel> GetClashes(ClashSearchProgress progress) {
        (var mainProviders, var otherProviders) = GetProviders();

        var clashDetector = new ClashDetector(_revitRepository, mainProviders, otherProviders, Tolerance / 304.8);
        return clashDetector.FindClashes(progress, Name);
    }

    /// <summary>
    /// Ищет коллизии, сохраняет отчёт и возвращает итог прогона для сводки.
    /// </summary>
    /// <remarks>
    /// Сравнение с прошлым прогоном идёт по тем же правилам, что и перенос статусов
    /// (<c>ClashModel.Equals</c> - по паре элементов в любом порядке), поэтому "новых" и "ушло"
    /// считаются ровно по тем коллизиям, для которых <c>ClashesMarker</c> не нашёл или потерял
    /// пару в старом отчёте.
    /// </remarks>
    /// <param name="progress">Окно хода поиска, общее для всех проверок прогона.</param>
    public CheckSearchResult SaveClashes(ClashSearchProgress progress) {
        var config = ClashesConfig.GetClashesConfig(_revitRepository.GetObjectName(), ReportName);
        var oldClashes = ClashesConfig.GetClashesConfig(_revitRepository.GetObjectName(), $"{_revitRepository.GetDocumentName()}_{Name}").Clashes;
        var newClashes = GetClashes(progress);

        int added = ClashesMarker.CountAdded(newClashes, oldClashes);
        int gone = ClashesMarker.CountGone(newClashes, oldClashes);

        config.Clashes = ClashesMarker.MarkSolvedClashes(newClashes, oldClashes).ToList();
        config.SaveProjectConfig();
        ClashCount = config.Clashes.Count;

        return new CheckSearchResult(Name, newClashes.Count, added, gone, oldClashes.Count > 0);
    }

    private void InitializeSelections(Check check = null) {
        FirstSelection = new SelectionViewModel(_revitRepository,
            _filtersConfig,
            _localizationService,
            check?.FirstSelection);
        SecondSelection = new SelectionViewModel(_revitRepository,
            _filtersConfig,
            _localizationService,
            check?.SecondSelection);
    }

    private void InitializeFilterProviders(Check check) {
        InitializeSelections(check);

        string firstFiles = FirstSelection.GetMissedFiles();
        if(!string.IsNullOrEmpty(firstFiles)) {
            ErrorText = _localizationService.GetLocalizedString(
                    "ChecksWindow.Validation.MissingFiles1", firstFiles)
                + Environment.NewLine;
        }
        string firstFilters = FirstSelection.GetMissedFilters();
        if(!string.IsNullOrEmpty(firstFilters)) {
            ErrorText += _localizationService.GetLocalizedString(
                    "ChecksWindow.Validation.MissingFilters1", firstFilters)
                + Environment.NewLine;
        }

        string secondFiles = SecondSelection.GetMissedFiles();
        if(!string.IsNullOrEmpty(secondFiles)) {
            ErrorText = _localizationService.GetLocalizedString(
                    "ChecksWindow.Validation.MissingFiles2", secondFiles)
                + Environment.NewLine;
        }
        string secondFilters = SecondSelection.GetMissedFilters();
        if(!string.IsNullOrEmpty(secondFilters)) {
            ErrorText += _localizationService.GetLocalizedString(
                    "ChecksWindow.Validation.MissingFilters2", secondFilters)
                + Environment.NewLine;
        }

        // Пустой конфиг без файла - поиск ещё не запускали; файл с нулём коллизий - запускали.
        var report = ClashesConfig.GetClashesConfig(_revitRepository.GetObjectName(), ReportName);
        ClashCount = File.Exists(report.ProjectConfigPath) ? report.Clashes.Count : null;
    }

    /// <remarks>
    /// Окно собирается тем же ядром, что и с ленты: пустой конструктор окна не даёт ему ни
    /// словаря локализации, ни служб темы. Ошибки ловятся здесь - команда вызывается из окна,
    /// и непойманное исключение пропадает в WPF молча, кнопка выглядит мёртвой.
    /// </remarks>
    private void ShowClashes() {
        try {
            var window = NavigatorWindowFactory.Create(
                _revitRepository.UiApplication, _revitRepository, ReportName);
            window.Show(_revitRepository.UiApplication.MainWindowHandle);
            NavigatorOpened?.Invoke(this, EventArgs.Empty);
        } catch(Exception exception) {
            MessageWindow.ShowError(
                exception.ToString(),
                _localizationService.GetLocalizedString("Navigator.Title"),
                _localizationService);
        }
    }

    private bool CanShowClashes() {
        return HasReport;
    }
}
