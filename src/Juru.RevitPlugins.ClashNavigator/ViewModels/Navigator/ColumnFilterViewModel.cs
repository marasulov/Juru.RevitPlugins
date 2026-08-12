using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

using dosymep.WPF.Commands;
using dosymep.WPF.ViewModels;

namespace Juru.RevitPlugins.ClashNavigator.ViewModels.Navigator;

/// <summary>
/// Состояние выпадающего фильтра для одной колонки таблицы коллизий.
/// </summary>
internal class ColumnFilterViewModel : BaseViewModel {
    private readonly Action _onApply;
    private bool _isOpen;
    private string _searchText = string.Empty;

    public ColumnFilterViewModel(string propertyName, Action onApply) {
        PropertyName = propertyName ?? throw new ArgumentNullException(nameof(propertyName));
        _onApply = onApply ?? throw new ArgumentNullException(nameof(onApply));

        OpenCommand = RelayCommand.Create(() => IsOpen = !IsOpen);
        ApplyCommand = RelayCommand.Create(Apply);
        SelectAllCommand = RelayCommand.Create(() => SetAll(true));
        ClearCommand = RelayCommand.Create(() => SetAll(false));
    }

    /// <summary>
    /// Название свойства <see cref="IClashViewModel"/>, по которому фильтруется колонка.
    /// </summary>
    public string PropertyName { get; }

    public ObservableCollection<ColumnFilterOptionViewModel> Options { get; } = [];

    public string SearchText {
        get => _searchText;
        set {
            RaiseAndSetIfChanged(ref _searchText, value);
            RaisePropertyChanged(nameof(FilteredOptions));
            RaisePropertyChanged(nameof(IsActive));
        }
    }

    /// <summary>
    /// Значения из <see cref="Options"/>, содержащие текст поиска <see cref="SearchText"/>.
    /// </summary>
    public IEnumerable<ColumnFilterOptionViewModel> FilteredOptions => string.IsNullOrWhiteSpace(SearchText)
        ? Options
        : Options.Where(o => o.Value?.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0);

    public bool IsOpen {
        get => _isOpen;
        set => RaiseAndSetIfChanged(ref _isOpen, value);
    }

    /// <summary>
    /// True, если фильтр реально что-то скрывает: либо снята отметка хотя бы с одного значения,
    /// либо задан текст поиска (в этом случае непопадающие под поиск значения тоже считаются скрытыми,
    /// даже если формально остались отмеченными).
    /// </summary>
    public bool IsActive => !string.IsNullOrWhiteSpace(SearchText) || Options.Any(o => !o.IsChecked);

    public ICommand OpenCommand { get; }
    public ICommand ApplyCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand ClearCommand { get; }

    /// <summary>
    /// Отмеченные значения среди тех, что проходят текущий поиск.
    /// Значения, скрытые поиском, не считаются выбранными, даже если у них стоит отметка.
    /// </summary>
    public HashSet<string> SelectedValues => [.. FilteredOptions.Where(o => o.IsChecked).Select(o => o.Value)];

    public bool Matches(string value) {
        return !IsActive || SelectedValues.Contains(value ?? string.Empty);
    }

    /// <summary>
    /// Обновляет список доступных значений, сохраняя текущий выбор для значений, которые остались.
    /// </summary>
    public void SetOptions(IEnumerable<string> values) {
        var previouslyChecked = Options.Count == 0
            ? null
            : Options.Where(o => o.IsChecked).Select(o => o.Value).ToHashSet();

        Options.Clear();
        foreach(string value in values.Distinct().OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase)) {
            Options.Add(new ColumnFilterOptionViewModel {
                Value = value,
                IsChecked = previouslyChecked is null || previouslyChecked.Contains(value)
            });
        }
        RaisePropertyChanged(nameof(FilteredOptions));
    }

    /// <summary>
    /// Снимает фильтр: все значения отмечены, текст поиска пуст.
    /// Таблицу при этом не перефильтровывает - это делает вызывающий, один раз для всех колонок.
    /// </summary>
    public void Reset() {
        _searchText = string.Empty;
        SetAll(true);
        RaisePropertyChanged(nameof(SearchText));
        RaisePropertyChanged(nameof(FilteredOptions));
        RaisePropertyChanged(nameof(IsActive));
    }

    private void SetAll(bool isChecked) {
        foreach(var option in Options) {
            option.IsChecked = isChecked;
        }
    }

    private void Apply() {
        IsOpen = false;
        RaisePropertyChanged(nameof(IsActive));
        _onApply();
    }
}
