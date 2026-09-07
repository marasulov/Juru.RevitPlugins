using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

using Autodesk.Revit.DB;

using dosymep.SimpleServices;
using dosymep.WPF.Commands;
using dosymep.WPF.ViewModels;

using Juru.RevitPlugins.ClashNavigator.Models;
using Juru.RevitPlugins.ClashNavigator.Models.FilterModel;
using Juru.RevitPlugins.ClashNavigator.Models.Interfaces;
using Juru.RevitPlugins.ClashNavigator.Services;
using Juru.RevitPlugins.ClashNavigator.Views.Checks;

namespace Juru.RevitPlugins.ClashNavigator.ViewModels.ClashDetective;
internal class SelectionViewModel : BaseViewModel {
    private readonly RevitRepository _revitRepository;
    private readonly ILocalizationService _localizationService;
    private readonly SelectionConfig _selectionConfig;
    private FiltersConfig _filterConfig;
    private ObservableCollection<FileViewModel> _allFiles = [];
    private ObservableCollection<FileViewModel> _selectedFiles = [];
    private ObservableCollection<FilterProviderViewModel> _allProviders = [];
    private ObservableCollection<FilterProviderViewModel> _selectedProviders = [];

    public SelectionViewModel(RevitRepository revitRepository,
        FiltersConfig filterConfig,
        ILocalizationService localizationService,
        SelectionConfig selectionConfig = null) {

        _revitRepository = revitRepository ?? throw new ArgumentNullException(nameof(revitRepository));
        _filterConfig = filterConfig ?? throw new ArgumentNullException(nameof(filterConfig));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _selectionConfig = selectionConfig;
        if(selectionConfig == null) {
            InitializeFiles();
            InitializeProviders();
        } else {
            InitializeFiles(selectionConfig);
            InitializeProviders(selectionConfig);
        }

        SelectFilesCommand = RelayCommand.Create(SelectFiles, CanSelectFiles);
        SelectProvidersCommand = RelayCommand.Create(SelectProviders, CanSelectProviders);
    }

    public ICommand SelectProvidersCommand { get; }

    public ICommand SelectFilesCommand { get; }

    public ObservableCollection<FileViewModel> AllFiles {
        get => _allFiles;
        private set => RaiseAndSetIfChanged(ref _allFiles, value);
    }

    public ObservableCollection<FileViewModel> SelectedFiles {
        get => _selectedFiles;
        set => RaiseAndSetIfChanged(ref _selectedFiles, value);
    }

    public ObservableCollection<FilterProviderViewModel> AllProviders {
        get => _allProviders;
        private set => RaiseAndSetIfChanged(ref _allProviders, value);
    }

    public ObservableCollection<FilterProviderViewModel> SelectedProviders {
        get => _selectedProviders;
        set => RaiseAndSetIfChanged(ref _selectedProviders, value);
    }

    public IEnumerable<IProvider> GetProviders() {
        return SelectedFiles
            .SelectMany(item => SelectedProviders
                .Select(p => p.GetProvider(item.Doc, item.Transform)));
    }

    public SelectionConfig GetCheckSettings() {
        return new SelectionConfig() {
            Files = [.. SelectedFiles.Select(item => item.Name)],
            Filters = [.. SelectedProviders.Select(item => item.Name)]
        };
    }

    public string GetMissedFiles() {
        string[] missedFiles = _selectionConfig.Files
           .Where(item => !AllFiles.Any(p => p.Name.Equals(item)))
           .ToArray();
        return string.Join(",", missedFiles);
    }

    public string GetMissedFilters() {
        string[] missedFilters = _selectionConfig.Filters
           .Where(item => !AllProviders.Any(p => p.Name.Equals(item)))
           .ToArray();
        return string.Join(",", missedFilters);
    }

    private void InitializeFiles() {
        AllFiles = new ObservableCollection<FileViewModel>(_revitRepository.GetRevitLinkInstances()
            .Select(item => new FileViewModel(item.GetLinkDocument(), item.GetTransform()))) {
            new(_revitRepository.Doc, Transform.Identity)
        };
    }

    private void InitializeFiles(SelectionConfig selectionConfig) {
        InitializeFiles();

        SelectedFiles = new ObservableCollection<FileViewModel>(AllFiles
            .Where(item => selectionConfig.Files.Any(
                f => f.Equals(item.Name, StringComparison.CurrentCultureIgnoreCase))));
    }

    private void InitializeProviders() {
        AllProviders = new ObservableCollection<FilterProviderViewModel>(
            _filterConfig.Filters
            .Select(item => new FilterProviderViewModel(item)));
    }

    private void InitializeProviders(SelectionConfig selectionConfig) {
        InitializeProviders();
        SelectedProviders = new ObservableCollection<FilterProviderViewModel>(AllProviders
            .Where(item => selectionConfig.Filters.Any(
                f => f.Equals(item.Name, StringComparison.CurrentCultureIgnoreCase))));
    }

    private void SelectFiles() {
        var vm = new SelectableNamesViewModel(AllFiles, SelectedFiles) {
            Title = _localizationService.GetLocalizedString("Selection.SelectFilesTitle")
        };
        var window = new SelectableNamesWindow(vm, _localizationService);
        if(window.ShowDialog() == true) {
            SelectedFiles = [.. vm.GetSelectedEntities<FileViewModel>()];
        }
    }

    private bool CanSelectFiles() {
        return AllFiles is not null && SelectedFiles is not null;
    }

    /// <summary>
    /// Пока наборов нет, выбирать в диалоге нечего, и единственный осмысленный шаг отсюда -
    /// создать набор. Поэтому диалог умеет открыть редактор наборов и вернуться к выбору
    /// с уже пополненным списком.
    /// </summary>
    private void SelectProviders() {
        while(true) {
            var vm = new SelectableNamesViewModel(AllProviders, SelectedProviders) {
                Title = _localizationService.GetLocalizedString("Selection.SelectSearchSetsTitle")
            };
            var window = new SelectableNamesWindow(vm, _localizationService,
                _localizationService.GetLocalizedString("Selection.CreateSearchSet"));

            if(window.ShowDialog() == true) {
                SelectedProviders = [.. vm.GetSelectedEntities<FilterProviderViewModel>()];
                return;
            }
            if(!window.AddRequested) {
                return;
            }

            OpenSearchSets();
        }
    }

    private bool CanSelectProviders() {
        return AllProviders is not null && SelectedProviders is not null;
    }

    /// <summary>
    /// Открывает редактор наборов модально и перечитывает наборы после его закрытия: редактор
    /// пишет их в файл, и без перечитывания только что созданный набор в выборе не появится.
    /// </summary>
    private void OpenSearchSets() {
        SearchSetsWindowFactory
            .Create(_revitRepository.UiApplication, _revitRepository)
            .ShowDialog();

        string[] selectedNames = [.. SelectedProviders.Select(item => item.Name)];
        _filterConfig = FiltersConfig.GetFiltersConfig(
            _revitRepository.GetConfigRelativePath(), _revitRepository.Doc);
        InitializeProviders();
        SelectedProviders = [.. AllProviders.Where(item => selectedNames.Any(
            name => name.Equals(item.Name, StringComparison.CurrentCultureIgnoreCase)))];
    }
}
