using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

using Autodesk.Revit.DB;

using dosymep.Revit;
using dosymep.SimpleServices;
using dosymep.WPF.Commands;
using dosymep.WPF.ViewModels;

using Juru.RevitPlugins.ClashNavigator.Models;
using Juru.RevitPlugins.ClashNavigator.Models.FilterableValueProviders;
using Juru.RevitPlugins.ClashNavigator.Models.FilterModel;
using Juru.RevitPlugins.ClashNavigator.Models.Interfaces;
using Juru.RevitPlugins.ClashNavigator.ViewModels.ClashDetective;
using Juru.RevitPlugins.ClashNavigator.Views.Checks;

namespace Juru.RevitPlugins.ClashNavigator.ViewModels.SearchSet;
/// <summary>
/// Поисковый набор: категории элементов и дерево критериев, по которым они отбираются.
/// </summary>
internal class FilterViewModel : BaseViewModel, INamedEntity, IName {
    private readonly RevitRepository _revitRepository;
    private readonly ILocalizationService _localizationService;
    private ObservableCollection<CategoryViewModel> _selectedCategories = [];
    private string _name;
    private string _providersErrorText;
    private readonly List<string> _sourceFilePatterns;

    public FilterViewModel(RevitRepository revitRepository,
        ILocalizationService localizationService,
        Filter filter = null) {

        _revitRepository = revitRepository ?? throw new ArgumentNullException(nameof(revitRepository));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));

        AllCategories = [.. _revitRepository.GetCategories()
            .Where(item => item != null)
            .Select(item => new CategoryViewModel(item))
            .OrderBy(item => item.Name)];

        Name = filter?.Name ?? _localizationService.GetLocalizedString("SearchSets.DefaultFilterName");
        _sourceFilePatterns = filter?.SourceFilePatterns;
        if(filter?.CategoryIds != null) {
            SelectedCategories = [.. AllCategories.Where(
                item => filter.CategoryIds.Any(id => id == item.Id))];
        }
        // Параметры собираются до дерева критериев: правила сохранённого набора ищут в этом
        // списке свой параметр, чтобы он встал в выпадающий список выбранным.
        RefreshProviders();
        Set = new SetViewModel(this, _localizationService, filter?.Set);

        SelectCategoriesCommand = RelayCommand.Create(SelectCategories);
    }

    public string Name {
        get => _name;
        set => RaiseAndSetIfChanged(ref _name, value);
    }

    public ObservableCollection<CategoryViewModel> AllCategories { get; }

    public ObservableCollection<CategoryViewModel> SelectedCategories {
        get => _selectedCategories;
        private set => RaiseAndSetIfChanged(ref _selectedCategories, value);
    }

    /// <summary>
    /// Параметры, доступные правилам набора: общие для всех выбранных категорий, и рабочий набор.
    /// </summary>
    public ObservableCollection<IFilterableValueProvider> Providers { get; } = [];

    public SetViewModel Set { get; }

    /// <summary>
    /// Почему список параметров пуст или неполон. Ревит отказывается отдавать параметры
    /// отдельных категорий, и молчать об этом нельзя: правило не найдёт параметр в списке,
    /// а причина останется незаметной.
    /// </summary>
    public string ProvidersErrorText {
        get => _providersErrorText;
        private set => RaiseAndSetIfChanged(ref _providersErrorText, value);
    }

    public ICommand SelectCategoriesCommand { get; }

    public Category[] GetSelectedCategories() {
        return [.. SelectedCategories.Select(item => item.Category)];
    }

    /// <summary>
    /// Возвращает параметр из списка доступных, добавляя его туда, если его там ещё нет.
    /// </summary>
    public IFilterableValueProvider GetOrAddProvider(IFilterableValueProvider provider) {
        var existing = Providers.FirstOrDefault(item => item.Equals(provider));
        if(existing != null) {
            return existing;
        }
        provider.RevitRepository = _revitRepository;
        Providers.Add(provider);
        return provider;
    }

    public Filter GetFilter() {
        return new Filter(_revitRepository) {
            Name = Name,
            CategoryIds = [.. SelectedCategories.Select(item => item.Id)],
            Set = (Set) Set.GetCriterion(),
            SourceFilePatterns = _sourceFilePatterns
        };
    }

    /// <summary>
    /// Состояние набора одной строкой - см. <see cref="CriterionViewModel.GetSignature" />.
    /// </summary>
    public string GetSignature() {
        string categories = string.Join(",", SelectedCategories
            .Select(item => item.Id.GetIdValue())
            .OrderBy(item => item));
        return $"{Name}|{categories}|{Set.GetSignature()}";
    }

    public string GetErrorText() {
        if(string.IsNullOrWhiteSpace(Name)) {
            return _localizationService.GetLocalizedString("SearchSets.Validation.NoName");
        }
        if(SelectedCategories.Count == 0) {
            return _localizationService.GetLocalizedString("SearchSets.Validation.NoCategories", Name);
        }
        string setError = Set.GetErrorText();
        return setError == null
            ? null
            : _localizationService.GetLocalizedString("SearchSets.Validation.SetPrefix", Name, setError);
    }

    private void SelectCategories() {
        var vm = new SelectableNamesViewModel(AllCategories, SelectedCategories) {
            Title = _localizationService.GetLocalizedString("SearchSets.SelectCategoriesTitle"),
            CanHideUnselected = true
        };
        var window = new SelectableNamesWindow(vm, _localizationService);

        if(window.ShowDialog() == true) {
            SelectedCategories = [.. vm.GetSelectedEntities<CategoryViewModel>()];
            RefreshProviders();
        }
    }

    /// <summary>
    /// Пересобирает список параметров под выбранные категории. Параметры, на которые уже
    /// ссылаются правила, остаются в списке, даже если их категорию убрали: иначе выпадающий
    /// список правила опустел бы и правило потеряло бы параметр молча.
    /// </summary>
    private void RefreshProviders() {
        var used = GetUsedProviders().ToList();
        var categories = GetSelectedCategories();
        IEnumerable<IFilterableValueProvider> parameters;
        if(categories.Length == 0) {
            // Список параметров общий для выбранных категорий: без единой выбранной категории
            // получить его неоткуда. Явное сообщение вместо молча пустого списка - иначе кажется,
            // что "Добавить правило" сломано, хотя дело в порядке действий.
            parameters = [];
            ProvidersErrorText = used.Count == 0
                ? _localizationService.GetLocalizedString("SearchSets.SelectCategoriesFirst")
                : null;
        } else {
            try {
                // Рабочий набор есть у элемента любой категории, поэтому он в списке всегда, когда
                // выбрана хоть одна категория.
                parameters = [
                    .. _revitRepository.GetParameters(_revitRepository.Doc, categories),
                    new WorksetValueProvider(_revitRepository)
                ];
                ProvidersErrorText = null;
            } catch(Exception exception) {
                parameters = [];
                ProvidersErrorText = _localizationService.GetLocalizedString(
                    "SearchSets.ParametersError", exception.Message);
            }
        }

        Providers.Clear();
        foreach(var provider in parameters
            .Union(used)
            .Distinct()
            .OrderBy(item => item.Name)) {

            provider.RevitRepository = _revitRepository;
            Providers.Add(provider);
        }

        // GetParameters отбирает только параметры, общие всем выбранным категориям сразу
        // (см. её отсечение "item.Count() > categories.Count() - 1" в RevitRepository).
        // Категории без единого общего параметра (например, "Трубы" и "Двери") дают пустой
        // список на полностью законных основаниях, но выглядит это как сломанное правило.
        if(Providers.Count == 0 && categories.Length > 0) {
            ProvidersErrorText = _localizationService.GetLocalizedString(categories.Length == 1
                ? "SearchSets.NoParametersSingle"
                : "SearchSets.NoParametersMultiple");
        }
    }

    private IEnumerable<IFilterableValueProvider> GetUsedProviders() {
        return Set is null ? [] : GetUsedProviders(Set);
    }

    private IEnumerable<IFilterableValueProvider> GetUsedProviders(SetViewModel set) {
        foreach(var criterion in set.Criteria) {
            switch(criterion) {
                case SetViewModel nestedSet: {
                    foreach(var provider in GetUsedProviders(nestedSet)) {
                        yield return provider;
                    }
                    break;
                }
                case RuleViewModel rule when rule.SelectedProvider != null: {
                    yield return rule.SelectedProvider;
                    break;
                }
            }
        }
    }
}
