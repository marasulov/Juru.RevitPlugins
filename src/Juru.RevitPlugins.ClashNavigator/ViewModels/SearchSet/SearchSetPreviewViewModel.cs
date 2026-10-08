using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

using dosymep.SimpleServices;
using dosymep.WPF.Commands;
using dosymep.WPF.ViewModels;

using Juru.RevitPlugins.ClashNavigator.Models;
using Juru.RevitPlugins.ClashNavigator.Models.FilterModel;
using Juru.RevitPlugins.ClashNavigator.Models.Interfaces;

namespace Juru.RevitPlugins.ClashNavigator.ViewModels.SearchSet;
/// <summary>
/// Просмотр поискового набора: какие элементы он отбирает - таблицей и на 3D-виде коллизий.
/// </summary>
/// <remarks>
/// Набор берётся таким, каким он настроен в окне сейчас, без сохранения в файл: проверить
/// правило хочется до того, как сохранять. Окно немодальное, поэтому всё, что меняет Ревит
/// (фильтры вида, подрезка, выделение), идёт через внешнее событие репозитория. Элементы и
/// значения параметров читаются сразу: так таблица заполняется, даже когда окно наборов открыто
/// модально из окна проверок и внешнее событие ждёт его закрытия.
/// </remarks>
internal class SearchSetPreviewViewModel : BaseViewModel {
    private readonly RevitRepository _revitRepository;
    private readonly ILocalizationService _localizationService;
    private readonly Filter _filter;
    private readonly IReadOnlyList<IFilterableValueProvider> _providers;
    private ObservableCollection<SearchSetElementViewModel> _elements = [];
    private SearchSetElementViewModel _selectedElement;
    private bool _inverted;
    private string _statusText;
    private string _errorText;

    public SearchSetPreviewViewModel(RevitRepository revitRepository,
        ILocalizationService localizationService,
        Filter filter) {

        _revitRepository = revitRepository ?? throw new ArgumentNullException(nameof(revitRepository));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _filter = filter ?? throw new ArgumentNullException(nameof(filter));
        _providers = [.. _filter.GetProviders().Where(item => item != null)];
        ParameterNames = [.. _providers.Select(item => item.Name)];
        Name = _filter.Name;

        ShowSetCommand = RelayCommand.Create(ShowSet);
        ShowElementCommand = RelayCommand.Create(ShowElement, CanShowElement);
        NextCommand = RelayCommand.Create(SelectNext, CanSelectNext);
        PreviousCommand = RelayCommand.Create(SelectPrevious, CanSelectPrevious);

        LoadElements();
    }

    public string Name { get; }

    /// <summary>
    /// Заголовки колонок с параметрами правил, в порядке <see cref="SearchSetElementViewModel.Values" />.
    /// </summary>
    public IReadOnlyList<string> ParameterNames { get; }

    /// <summary>
    /// Показывать обратный набор: элементы тех же категорий, не подходящие под правила.
    /// </summary>
    public bool Inverted {
        get => _inverted;
        set {
            if(_inverted == value) {
                return;
            }
            RaiseAndSetIfChanged(ref _inverted, value);
            LoadElements();
            ShowSet();
        }
    }

    public ObservableCollection<SearchSetElementViewModel> Elements {
        get => _elements;
        private set => RaiseAndSetIfChanged(ref _elements, value);
    }

    public SearchSetElementViewModel SelectedElement {
        get => _selectedElement;
        set => RaiseAndSetIfChanged(ref _selectedElement, value);
    }

    public string StatusText {
        get => _statusText;
        private set => RaiseAndSetIfChanged(ref _statusText, value);
    }

    public string ErrorText {
        get => _errorText;
        private set => RaiseAndSetIfChanged(ref _errorText, value);
    }

    public ICommand ShowSetCommand { get; }
    public ICommand ShowElementCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand PreviousCommand { get; }

    /// <summary>
    /// Снимает с вида фильтры показа набора - при закрытии окна.
    /// </summary>
    public void Close() {
        _revitRepository.QueueClearSearchSetPreview();
    }

    private void LoadElements() {
        ErrorText = null;
        SelectedElement = null;
        try {
            var docInfos = _revitRepository.DocInfos;
            Elements = [.. _revitRepository.GetSearchSetElements(_filter, Inverted)
                .Select(model => new SearchSetElementViewModel(model, model.GetElement(docInfos), _providers))];
        } catch(Exception exception) when(exception is Autodesk.Revit.Exceptions.ApplicationException
                                              or InvalidOperationException) {
            Elements = [];
            ErrorText = _localizationService.GetLocalizedString("SearchSet.LoadError", exception.Message);
        }
        StatusText = _localizationService.GetLocalizedString("SearchSet.ElementsCount", Elements.Count);
    }

    private void ShowSet() {
        _revitRepository.QueueShowSearchSet(_filter, Inverted, shown => {
            ErrorText = shown ? null : _localizationService.GetLocalizedString("SearchSet.ShowSetError");
        });
    }

    private void ShowElement() {
        _revitRepository.QueueShowSearchSetElement(SelectedElement.Model, shown => {
            ErrorText = shown ? null : _localizationService.GetLocalizedString("SearchSet.ShowElementError");
        });
    }

    private bool CanShowElement() {
        return SelectedElement != null;
    }

    private void SelectNext() {
        SelectedElement = Elements[SelectedElement is null ? 0 : Elements.IndexOf(SelectedElement) + 1];
        ShowElement();
    }

    private bool CanSelectNext() {
        return Elements.Count > 0
               && (SelectedElement is null || Elements.IndexOf(SelectedElement) < Elements.Count - 1);
    }

    private void SelectPrevious() {
        SelectedElement = Elements[SelectedElement is null
            ? Elements.Count - 1
            : Elements.IndexOf(SelectedElement) - 1];
        ShowElement();
    }

    private bool CanSelectPrevious() {
        return Elements.Count > 0
               && (SelectedElement is null || Elements.IndexOf(SelectedElement) > 0);
    }
}
