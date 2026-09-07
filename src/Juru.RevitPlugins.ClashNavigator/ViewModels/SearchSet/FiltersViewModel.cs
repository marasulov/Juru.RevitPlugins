using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

using dosymep.Bim4Everyone;
using dosymep.SimpleServices;
using dosymep.WPF.Commands;
using dosymep.WPF.ViewModels;

using Juru.RevitPlugins.ClashNavigator.Models;
using Juru.RevitPlugins.ClashNavigator.Models.FilterModel;
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

        AddFilterCommand = RelayCommand.Create(AddFilter);
        RemoveFilterCommand = RelayCommand.Create<IList>(RemoveFilter, CanRemoveFilter);
        SaveCommand = RelayCommand.Create(SaveConfig, CanSave);
        SaveAsCommand = RelayCommand.Create(SaveAsConfig, CanSave);
        LoadCommand = RelayCommand.Create(LoadConfig);

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
        ShowMessage(LocalizationService.GetLocalizedString("SearchSets.SetsSaved"));
    }

    private void SaveAsConfig() {
        RenewConfig();
        new ConfigSaverService(_revitRepository, SaveFileDialogService).Save(_filtersConfig);
        ShowMessage(LocalizationService.GetLocalizedString("SearchSets.SetsSaved"));
    }

    private void LoadConfig() {
        var loader = new ConfigLoaderService(
            _revitRepository, LocalizationService, OpenFileDialogService, MessageBoxService);
        var config = loader.Load<FiltersConfig>();
        loader.CheckConfig(config);

        var nameResolver = new NameResolver<FilterViewModel>(AllFilters, [.. InitializeFilters(config)]);
        SetAllFilters(nameResolver.GetCollection());
        SelectedFilter = AllFilters.FirstOrDefault();
        ShowMessage(LocalizationService.GetLocalizedString("SearchSets.SetsLoaded"));
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
