using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

using dosymep.SimpleServices;
using dosymep.WPF.Commands;

using Juru.RevitPlugins.ClashNavigator.Models.Evaluators;
using Juru.RevitPlugins.ClashNavigator.Models.FilterModel;
using Juru.RevitPlugins.ClashNavigator.Models.Interfaces;

namespace Juru.RevitPlugins.ClashNavigator.ViewModels.SearchSet;
/// <summary>
/// Правило поискового набора: параметр, условие и значение.
/// </summary>
internal class RuleViewModel : CriterionViewModel {
    private readonly FilterViewModel _filter;
    private readonly ILocalizationService _localizationService;
    private IFilterableValueProvider _selectedProvider;
    private RuleEvaluator _selectedEvaluator;
    private ObservableCollection<RuleEvaluator> _evaluators = [];
    private ObservableCollection<string> _values = [];
    private string _value;
    private bool _valuesLoaded;

    public RuleViewModel(FilterViewModel filter,
        ILocalizationService localizationService,
        Rule rule = null) {

        _filter = filter ?? throw new ArgumentNullException(nameof(filter));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        LoadValuesCommand = RelayCommand.Create(LoadValues, CanLoadValues);

        if(rule?.Provider is { } provider) {
            // Параметр из сохранённого набора мог остаться без своей категории - иначе правило
            // молча теряло бы параметр, стоило открыть набор с изменившимся составом категорий.
            SelectedProvider = _filter.GetOrAddProvider(provider);
            SelectedEvaluator = Evaluators.FirstOrDefault(
                item => item.Evaluator == rule.Evaluator?.Evaluator) ?? Evaluators.FirstOrDefault();
            Value = rule.Value?.DisplayValue;
        }
    }

    public override bool IsSet => false;

    public ObservableCollection<IFilterableValueProvider> Providers => _filter.Providers;

    public IFilterableValueProvider SelectedProvider {
        get => _selectedProvider;
        set {
            RaiseAndSetIfChanged(ref _selectedProvider, value);
            // У параметра другого типа данных набор условий свой, поэтому старое условие не переносится.
            var evaluators = value == null ? [] : value.GetRuleEvaluators().ToArray();
            foreach(var evaluator in evaluators) {
                EvaluatorNames.Localize(_localizationService, evaluator);
            }
            Evaluators = [.. evaluators];
            SelectedEvaluator = Evaluators.FirstOrDefault();
        }
    }

    public ObservableCollection<RuleEvaluator> Evaluators {
        get => _evaluators;
        private set => RaiseAndSetIfChanged(ref _evaluators, value);
    }

    public RuleEvaluator SelectedEvaluator {
        get => _selectedEvaluator;
        set {
            RaiseAndSetIfChanged(ref _selectedEvaluator, value);
            ResetValues();
            RaisePropertyChanged(nameof(IsValueEnabled));
            if(!IsValueEnabled) {
                Value = null;
            }
        }
    }

    public string Value {
        get => _value;
        set => RaiseAndSetIfChanged(ref _value, value);
    }

    /// <summary>
    /// Значения параметра, встречающиеся в моделях. Список заполняется только по требованию:
    /// его сбор обходит все документы проверки и стоит секунд, а нужен не в каждом правиле.
    /// </summary>
    public ObservableCollection<string> Values {
        get => _values;
        private set => RaiseAndSetIfChanged(ref _values, value);
    }

    /// <summary>
    /// У условий "имеет значение" и "без значения" значения нет.
    /// </summary>
    public bool IsValueEnabled =>
        SelectedEvaluator != null
        && SelectedEvaluator.Evaluator is not RuleEvaluators.FilterHasValue
            and not RuleEvaluators.FilterHasNoValue;

    public ICommand LoadValuesCommand { get; }

    public override Criterion GetCriterion() {
        return new Rule() {
            Provider = SelectedProvider,
            Evaluator = SelectedEvaluator,
            // Без параметра правило не сохранить (см. GetErrorText), но копия набора собирается
            // и из недозаполненного - там правило просто останется пустым.
            Value = SelectedProvider?.GetParamValueFormString(IsValueEnabled ? Value : null)
        };
    }

    public override string GetSignature() {
        return $"R[{SelectedProvider?.GetType().Name}:{SelectedProvider?.Name}|{SelectedEvaluator?.Evaluator}|{(IsValueEnabled ? Value : null)}]";
    }

    public override string GetErrorText() {
        if(SelectedProvider is null) {
            return _localizationService.GetLocalizedString("SearchSets.Validation.RuleNoParameter");
        }
        if(SelectedEvaluator is null) {
            return _localizationService.GetLocalizedString("SearchSets.Validation.RuleNoEvaluator");
        }
        return IsValueEnabled && string.IsNullOrWhiteSpace(Value)
            ? _localizationService.GetLocalizedString(
                "SearchSets.Validation.RuleNoValue", SelectedProvider.Name)
            : null;
    }

    private void ResetValues() {
        _valuesLoaded = false;
        Values = [];
    }

    private void LoadValues() {
        IEnumerable<string> values;
        try {
            values = SelectedProvider
                .GetValues(_filter.GetSelectedCategories(), SelectedEvaluator)
                .Select(item => item.DisplayValue)
                .Where(item => !string.IsNullOrEmpty(item))
                .Distinct()
                .ToArray();
        } catch(Autodesk.Revit.Exceptions.ApplicationException) {
            // Параметр может не читаться в части документов - тогда подсказать нечего,
            // но правило остаётся рабочим: значение вписывается руками.
            values = [];
        }
        Values = [.. values];
        _valuesLoaded = true;
    }

    private bool CanLoadValues() {
        return !_valuesLoaded && IsValueEnabled && SelectedProvider != null;
    }
}
