using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using System.Xml;
using System.Xml.Linq;

using dosymep.Bim4Everyone;
using dosymep.SimpleServices;
using dosymep.WPF.Commands;
using dosymep.WPF.ViewModels;

using Juru.RevitPlugins.ClashNavigator.Models;
using Juru.RevitPlugins.ClashNavigator.Models.FilterModel;
using Juru.RevitPlugins.ClashNavigator.Services.NavisworksImport;
using Juru.RevitPlugins.ClashNavigator.ViewModels.Services;

namespace Juru.RevitPlugins.ClashNavigator.ViewModels.SearchSet;
/// <summary>
/// Окно поисковых наборов: наборы, которыми проверки набирают элементы для сравнения.
/// </summary>
internal class FiltersViewModel : BaseViewModel {
    private readonly RevitRepository _revitRepository;
    private readonly FiltersConfig _filtersConfig;
    private ObservableCollection<FilterViewModel> _allFilters = [];
    private CollectionViewSource _filters;
    private FilterViewModel _selectedFilter;
    private string _filtersFilter;
    private string _errorText;
    private string _messageText;
    private string _savedSignature;

    public FiltersViewModel(RevitRepository revitRepository,
        IOpenFileDialogService openFileDialogService,
        ISaveFileDialogService saveFileDialogService,
        IMessageBoxService messageBoxService,
        ILocalizationService localizationService,
        FiltersConfig filtersConfig) {

        _revitRepository = revitRepository ?? throw new ArgumentNullException(nameof(revitRepository));
        OpenFileDialogService = openFileDialogService ?? throw new ArgumentNullException(nameof(openFileDialogService));
        SaveFileDialogService = saveFileDialogService ?? throw new ArgumentNullException(nameof(saveFileDialogService));
        MessageBoxService = messageBoxService ?? throw new ArgumentNullException(nameof(messageBoxService));
        LocalizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _filtersConfig = filtersConfig ?? throw new ArgumentNullException(nameof(filtersConfig));

        SetAllFilters(InitializeFilters(_filtersConfig));
        SelectedFilter = AllFilters.FirstOrDefault();
        _savedSignature = GetSignature();

        AddFilterCommand = RelayCommand.Create(AddFilter);
        RemoveFilterCommand = RelayCommand.Create<IList>(RemoveFilter, CanRemoveFilter);
        SaveCommand = RelayCommand.Create(SaveConfig, CanSave);
        SaveAsCommand = RelayCommand.Create(() => IgnoreCancel(SaveAsConfig), CanSave);
        LoadCommand = RelayCommand.Create(() => IgnoreCancel(LoadConfig));
        ExportNavisworksCommand = RelayCommand.Create(() => IgnoreCancel(ExportNavisworks), CanSave);
        SaveAndCloseCommand = RelayCommand.Create(SaveAndClose, CanSave);
        RenameFilterCommand = RelayCommand.Create(RenameFilter, HasSelectedFilter);
        CopyFilterCommand = RelayCommand.Create(CopyFilter, HasSelectedFilter);
        PreviewCommand = RelayCommand.Create(Preview, CanPreview);

        PropertyChanged += FiltersFilterPropertyChanged;
    }

    public IOpenFileDialogService OpenFileDialogService { get; }
    public ISaveFileDialogService SaveFileDialogService { get; }
    public IMessageBoxService MessageBoxService { get; }
    public ILocalizationService LocalizationService { get; }

    public ICommand AddFilterCommand { get; }
    public ICommand RemoveFilterCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand SaveAsCommand { get; }
    public ICommand LoadCommand { get; }
    public ICommand ExportNavisworksCommand { get; }
    public ICommand SaveAndCloseCommand { get; }
    public ICommand RenameFilterCommand { get; }
    public ICommand CopyFilterCommand { get; }
    public ICommand PreviewCommand { get; }

    /// <summary>
    /// Запрос имени набора. Окно назначает его себе: диалогу нужен владелец, иначе он
    /// уходит за окно наборов.
    /// </summary>
    public Func<FilterNameViewModel, bool> AskFilterName { get; set; }

    /// <summary>
    /// Открывает окно просмотра набора. Назначается окном - по той же причине, что и <see cref="AskFilterName" />.
    /// </summary>
    public Action<SearchSetPreviewViewModel> ShowPreview { get; set; }

    /// <summary>
    /// Наборы сохранены кнопкой ОК, окно можно закрывать.
    /// </summary>
    public event EventHandler CloseRequested;

    /// <summary>
    /// Есть ли изменения, не записанные в наборы проекта.
    /// </summary>
    public bool HasUnsavedChanges => GetSignature() != _savedSignature;

    public CollectionViewSource Filters {
        get => _filters;
        private set => RaiseAndSetIfChanged(ref _filters, value);
    }

    public FilterViewModel SelectedFilter {
        get => _selectedFilter;
        set => RaiseAndSetIfChanged(ref _selectedFilter, value);
    }

    public string FiltersFilter {
        get => _filtersFilter;
        set => RaiseAndSetIfChanged(ref _filtersFilter, value);
    }

    public string ErrorText {
        get => _errorText;
        set => RaiseAndSetIfChanged(ref _errorText, value);
    }

    public string MessageText {
        get => _messageText;
        set => RaiseAndSetIfChanged(ref _messageText, value);
    }

    private ObservableCollection<FilterViewModel> AllFilters {
        get => _allFilters;
        set => RaiseAndSetIfChanged(ref _allFilters, value);
    }

    private IEnumerable<FilterViewModel> InitializeFilters(FiltersConfig config) {
        foreach(var filter in config.Filters) {
            // У набора из файла ссылки на репозиторий нет: она не сериализуется.
            filter.RevitRepository = _revitRepository;
            filter.Set?.SetRevitRepository(_revitRepository);
            yield return new FilterViewModel(_revitRepository, LocalizationService, filter);
        }
    }

    private void SetAllFilters(IEnumerable<FilterViewModel> filters) {
        AllFilters = [.. filters.OrderBy(item => item.Name)];
        Filters = new CollectionViewSource() { Source = AllFilters };
        Filters.Filter += FiltersFilterHandler;
        Filters.SortDescriptions.Add(new SortDescription(nameof(FilterViewModel.Name), ListSortDirection.Ascending));
        Filters.IsLiveSortingRequested = true;
    }

    private void FiltersFilterPropertyChanged(object sender, PropertyChangedEventArgs e) {
        if(e.PropertyName == nameof(FiltersFilter)) {
            Filters?.View.Refresh();
        }
    }

    private void FiltersFilterHandler(object sender, FilterEventArgs e) {
        if(e.Item is FilterViewModel filter && !string.IsNullOrWhiteSpace(FiltersFilter)) {
            e.Accepted = filter.Name != null
                && filter.Name.IndexOf(FiltersFilter, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }
    }

    private void AddFilter() {
        var filter = new FilterViewModel(_revitRepository, LocalizationService);
        AllFilters.Add(filter);
        SelectedFilter = filter;
    }

    private void RemoveFilter(IList selectedItems) {
        foreach(var filter in selectedItems.OfType<FilterViewModel>().ToArray()) {
            AllFilters.Remove(filter);
        }
        SelectedFilter = AllFilters.FirstOrDefault();
    }

    private bool CanRemoveFilter(IList selectedItems) {
        return selectedItems is { Count: > 0 };
    }

    private void SaveConfig() {
        RenewConfig();
        _filtersConfig.SaveProjectConfig();
        _savedSignature = GetSignature();
        ShowMessage(LocalizationService.GetLocalizedString("SearchSets.SetsSaved"));
    }

    private void SaveAndClose() {
        SaveConfig();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Решает, закрывать ли окно: при несохранённых изменениях предлагает их сохранить, а если
    /// наборы сохранить нельзя - предупреждает, что изменения пропадут.
    /// </summary>
    /// <returns>false - окно оставить открытым.</returns>
    public bool ConfirmClose() {
        if(!HasUnsavedChanges) {
            return true;
        }

        string title = LocalizationService.GetLocalizedString("SearchSets.Title");
        if(CanSave()) {
            if(Views.MessageWindow.AskYesNo(
                   LocalizationService.GetLocalizedString("SearchSet.SavePrompt"), title, LocalizationService)) {
                SaveConfig();
            }
            return true;
        }

        return Views.MessageWindow.AskYesNo(
            LocalizationService.GetLocalizedString("SearchSet.CannotSavePrompt", ErrorText), title, LocalizationService);
    }

    private bool HasSelectedFilter() {
        return SelectedFilter != null;
    }

    private void RenameFilter() {
        var nameViewModel = new FilterNameViewModel(
            LocalizationService,
            LocalizationService.GetLocalizedString("SearchSet.RenameTitle"),
            AllFilters.Where(item => item != SelectedFilter).Select(item => item.Name),
            SelectedFilter.Name);
        if(AskFilterName?.Invoke(nameViewModel) == true) {
            SelectedFilter.Name = nameViewModel.Name.Trim();
        }
    }

    /// <summary>
    /// Копия выбранного набора под новым именем - со всеми категориями и правилами, в том числе
    /// ещё не сохранёнными.
    /// </summary>
    private void CopyFilter() {
        var names = AllFilters.Select(item => item.Name).ToList();
        string name = SelectedFilter.Name;
        for(int i = 1; names.Contains(name, StringComparer.CurrentCultureIgnoreCase); i++) {
            name = $"{SelectedFilter.Name} ({i})";
        }

        var nameViewModel = new FilterNameViewModel(
            LocalizationService,
            LocalizationService.GetLocalizedString("SearchSet.CopyTitle"),
            names,
            name);
        if(AskFilterName?.Invoke(nameViewModel) != true) {
            return;
        }

        var filter = SelectedFilter.GetFilter();
        filter.Name = nameViewModel.Name.Trim();
        var copy = new FilterViewModel(_revitRepository, LocalizationService, filter);
        AllFilters.Add(copy);
        SelectedFilter = copy;
    }

    private void Preview() {
        ShowPreview?.Invoke(new SearchSetPreviewViewModel(_revitRepository, LocalizationService, SelectedFilter.GetFilter()));
    }

    /// <summary>
    /// Смотреть можно только заполненный набор: из недозаполненного правила фильтр Ревита не собрать.
    /// </summary>
    private bool CanPreview() {
        return SelectedFilter != null && SelectedFilter.GetErrorText() is null;
    }

    private string GetSignature() {
        return string.Join("\n", AllFilters
            .Select(item => item.GetSignature())
            .OrderBy(item => item, StringComparer.Ordinal));
    }

    /// <summary>
    /// Закрытый пользователем файловый диалог - не ошибка: сервисы диалогов сообщают о нём
    /// исключением, и без перехвата оно уходило из команды в Ревит.
    /// </summary>
    private static void IgnoreCancel(Action action) {
        try {
            action();
        } catch(OperationCanceledException) {
        }
    }

    private void SaveAsConfig() {
        RenewConfig();
        new ConfigSaverService(_revitRepository, SaveFileDialogService).Save(_filtersConfig);
        ShowMessage(LocalizationService.GetLocalizedString("SearchSets.SetsSaved"));
    }

    /// <summary>
    /// Загружает наборы из файла плагина (.json) или из выгрузки Navisworks (.xml).
    /// </summary>
    private void LoadConfig() {
        var loader = new ConfigLoaderService(
            _revitRepository, LocalizationService, OpenFileDialogService, MessageBoxService);
        var file = loader.PickFile(
            "ClashConfig, Navisworks (*.json; *.xml)|*.json;*.xml|ClashConfig (*.json)|*.json|Navisworks (*.xml)|*.xml");

        if(string.Equals(file.Extension, ".xml", StringComparison.OrdinalIgnoreCase)) {
            ImportNavisworks(file);
            return;
        }

        var config = loader.LoadFrom<FiltersConfig>(file);
        loader.CheckConfig(config);
        AddFilters(InitializeFilters(config));
        ShowMessage(LocalizationService.GetLocalizedString("SearchSets.SetsLoaded"));
    }

    /// <summary>
    /// Выгружает наборы окна в XML поисковых наборов Navisworks.
    /// </summary>
    private void ExportNavisworks() {
        var report = new NavisworksImportReport();
        var filters = AllFilters.Select(item => item.GetFilter()).ToList();
        var document = new NavisworksExporter(_revitRepository.Doc, LocalizationService)
            .ExportSearchSets(filters, report);
        string path = new ConfigSaverService(_revitRepository, SaveFileDialogService)
            .SaveXml(document, "SearchSets");

        var summary = new StringBuilder();
        summary.AppendLine(LocalizationService.GetLocalizedString(
            "Export.SetsSummary", filters.Count - report.Skipped.Count, filters.Count, path));
        AppendList(summary, "Import.SkippedHeader", report.Skipped);
        AppendList(summary, "Import.NotesHeader", report.Notes);
        Views.MessageWindow.ShowInfo(
            summary.ToString().TrimEnd(),
            LocalizationService.GetLocalizedString("Export.Title"),
            LocalizationService);
    }

    private void AddFilters(IEnumerable<FilterViewModel> filters) {
        var nameResolver = new NameResolver<FilterViewModel>(AllFilters, [.. filters]);
        SetAllFilters(nameResolver.GetCollection());
        SelectedFilter = AllFilters.FirstOrDefault();
    }

    /// <summary>
    /// Импорт выгрузки Navisworks: поисковые наборы становятся наборами плагина, тесты
    /// Clash Detective - проверками по наборам этого окна.
    /// </summary>
    /// <remarks>
    /// Результат сохраняется сразу: проверки ссылаются на наборы по имени и читаются окном
    /// проверок из файла, поэтому наборы, оставшиеся только в этом окне, проверки бы не нашли.
    /// </remarks>
    private void ImportNavisworks(FileInfo file) {
        NavisExchange exchange;
        try {
            var document = XDocument.Load(file.FullName);
            if(!NavisExchange.IsNavisworksExchange(document)) {
                throw new FormatException();
            }
            exchange = NavisExchange.Parse(document);
        } catch(Exception exception) when(exception is XmlException or FormatException or IOException) {
            Views.MessageWindow.ShowError(
                LocalizationService.GetLocalizedString("Import.NotNavisworks", file.Name),
                LocalizationService.GetLocalizedString("Import.Title"),
                LocalizationService);
            return;
        }

        var report = new NavisworksImportReport();
        var importer = new NavisworksImporter(_revitRepository, LocalizationService);
        StringBuilder summary = new();

        if(exchange.SearchSets.Count > 0) {
            var filters = importer.ImportSearchSets(exchange, report);
            foreach(var filter in filters) {
                filter.Set?.SetRevitRepository(_revitRepository);
            }
            AddFilters(filters.Select(filter => new FilterViewModel(_revitRepository, LocalizationService, filter)));
            summary.AppendLine(LocalizationService.GetLocalizedString(
                "Import.SetsSummary", filters.Count, exchange.SearchSets.Count));
        }

        bool setsSaved = CanSave();
        if(setsSaved) {
            RenewConfig();
            _filtersConfig.SaveProjectConfig();
            _savedSignature = GetSignature();
        } else {
            summary.AppendLine(LocalizationService.GetLocalizedString("Import.SetsNotSaved", ErrorText));
        }

        if(exchange.ClashTests.Count > 0) {
            var checks = importer.ImportClashTests(exchange, [.. AllFilters.Select(item => item.GetFilter())], report);
            int added = AddChecks(checks);
            summary.AppendLine(LocalizationService.GetLocalizedString(
                "Import.ChecksSummary", added, exchange.ClashTests.Count));
        }

        if(exchange.SearchSets.Count == 0 && exchange.ClashTests.Count == 0) {
            summary.AppendLine(LocalizationService.GetLocalizedString("Import.Nothing"));
        }

        AppendList(summary, "Import.SkippedHeader", report.Skipped);
        AppendList(summary, "Import.NotesHeader", report.Notes);
        Views.MessageWindow.ShowInfo(
            summary.ToString().TrimEnd(),
            LocalizationService.GetLocalizedString("Import.Title"),
            LocalizationService);
    }

    /// <summary>
    /// Дописывает проверки в конфиг проверок открытого файла. Совпавшее имя получает суффикс,
    /// существующие проверки не трогаются.
    /// </summary>
    /// <returns>Сколько проверок добавлено.</returns>
    private int AddChecks(IReadOnlyCollection<Check> checks) {
        if(checks.Count == 0) {
            return 0;
        }

        var config = ChecksConfig.GetChecksConfig(_revitRepository.GetConfigRelativePath(), _revitRepository.Doc);
        var names = new HashSet<string>(config.Checks.Select(item => item.Name), StringComparer.CurrentCultureIgnoreCase);
        foreach(var check in checks) {
            string name = check.Name;
            for(int i = 1; names.Contains(name); i++) {
                name = $"{check.Name} ({i})";
            }
            check.Name = name;
            names.Add(name);
            config.Checks.Add(check);
        }
        config.RevitVersion = RevitVersionInfo.Version;
        config.SaveProjectConfig();
        return checks.Count;
    }

    private void AppendList(StringBuilder summary, string headerKey, IReadOnlyCollection<string> lines) {
        if(lines.Count == 0) {
            return;
        }
        summary.AppendLine();
        summary.AppendLine(LocalizationService.GetLocalizedString(headerKey, lines.Count));
        foreach(string line in lines) {
            summary.AppendLine("• " + line);
        }
    }

    private void RenewConfig() {
        _filtersConfig.Filters = [.. AllFilters.Select(item => item.GetFilter())];
        _filtersConfig.RevitVersion = RevitVersionInfo.Version;
    }

    /// <summary>
    /// Незаполненный набор сохранять нельзя: правило без параметра или без значения не станет
    /// фильтром Ревита, а молча сохранённый набор потом отберёт не то, что от него ждут.
    /// </summary>
    private bool CanSave() {
        if(AllFilters.Count == 0) {
            ErrorText = LocalizationService.GetLocalizedString("SearchSets.Validation.NoFilters");
            return false;
        }
        if(AllFilters.Select(item => item.Name)
            .GroupBy(item => item, StringComparer.CurrentCultureIgnoreCase)
            .Any(item => item.Count() > 1)) {
            ErrorText = LocalizationService.GetLocalizedString("SearchSets.Validation.NamesDuplicated");
            return false;
        }
        ErrorText = AllFilters.Select(item => item.GetErrorText()).FirstOrDefault(item => item != null);
        return ErrorText == null;
    }

    private void ShowMessage(string message) {
        MessageText = message;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (s, a) => {
            MessageText = null;
            timer.Stop();
        };
        timer.Start();
    }
}
