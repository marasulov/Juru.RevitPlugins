using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

using dosymep.SimpleServices;
using dosymep.WPF.Commands;

using Juru.RevitPlugins.ClashNavigator.Models.Evaluators;
using Juru.RevitPlugins.ClashNavigator.Models.FilterModel;

namespace Juru.RevitPlugins.ClashNavigator.ViewModels.SearchSet;
/// <summary>
/// Набор критериев: правила и вложенные наборы, объединённые условием И или ИЛИ.
/// </summary>
internal class SetViewModel : CriterionViewModel {
    private readonly FilterViewModel _filter;
    private readonly ILocalizationService _localizationService;
    private SetEvaluator _selectedEvaluator;

    public SetViewModel(FilterViewModel filter,
        ILocalizationService localizationService,
        Set set = null) {

        _filter = filter ?? throw new ArgumentNullException(nameof(filter));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        foreach(var evaluator in Evaluators) {
            EvaluatorNames.Localize(_localizationService, evaluator);
        }

        SelectedEvaluator = Evaluators.FirstOrDefault(
                item => item.Evaluator == (set?.SetEvaluator?.Evaluator ?? SetEvaluators.And))
            ?? Evaluators.FirstOrDefault();

        if(set != null) {
            foreach(var criterion in set.Criteria) {
                Add(criterion switch {
                    Set nestedSet => new SetViewModel(_filter, _localizationService, nestedSet),
                    Rule rule => new RuleViewModel(_filter, _localizationService, rule),
                    _ => (CriterionViewModel) null
                });
            }
        }

        AddRuleCommand = RelayCommand.Create(AddRule);
        AddSetCommand = RelayCommand.Create(AddSet);
        RemoveCriterionCommand = RelayCommand.Create<CriterionViewModel>(RemoveCriterion, CanRemoveCriterion);
    }

    public override bool IsSet => true;

    public ObservableCollection<SetEvaluator> Evaluators { get; } = [.. SetEvaluatorUtils.GetEvaluators()];

    public SetEvaluator SelectedEvaluator {
        get => _selectedEvaluator;
        set => RaiseAndSetIfChanged(ref _selectedEvaluator, value);
    }

    public ObservableCollection<CriterionViewModel> Criteria { get; } = [];

    public ICommand AddRuleCommand { get; }
    public ICommand AddSetCommand { get; }
    public ICommand RemoveCriterionCommand { get; }

    public override Criterion GetCriterion() {
        return new Set() {
            SetEvaluator = SelectedEvaluator,
            Criteria = [.. Criteria.Select(item => item.GetCriterion())]
        };
    }

    /// <summary>
    /// Пустой корневой набор - это "все элементы выбранных категорий", и так им и пользуются.
    /// Пустой вложенный - всегда недоделка: под ИЛИ он расширил бы отбор на всё, что есть
    /// в категориях, и набор молча отобрал бы не то, ради чего его заводили.
    /// </summary>
    public override string GetErrorText() {
        return Criteria.Count == 0
            ? Parent is null
                ? null
                : _localizationService.GetLocalizedString("SearchSets.Validation.NestedSetEmpty")
            : Criteria.Select(item => item.GetErrorText()).FirstOrDefault(item => item != null);
    }

    public override string GetSignature() {
        return $"S[{SelectedEvaluator?.Evaluator}:{string.Join(",", Criteria.Select(item => item.GetSignature()))}]";
    }

    private void Add(CriterionViewModel criterion) {
        if(criterion is null) {
            return;
        }
        criterion.Parent = this;
        Criteria.Add(criterion);
    }

    private void AddRule() {
        Add(new RuleViewModel(_filter, _localizationService));
    }

    private void AddSet() {
        Add(new SetViewModel(_filter, _localizationService));
    }

    private void RemoveCriterion(CriterionViewModel criterion) {
        Criteria.Remove(criterion);
    }

    private bool CanRemoveCriterion(CriterionViewModel criterion) {
        return criterion != null;
    }
}
