using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Juru.RevitPlugins.Schedules.Services;
using Juru.RevitPlugins.Schedules.Services.Translation;

namespace Juru.RevitPlugins.Schedules.ViewModels;

/// <summary>
///     Окно выбора спецификаций для английского комплекта.
/// </summary>
public sealed partial class SchedulesWindowViewModel : ObservableObject
{
    private readonly ICollectionView scheduleView;
    private readonly ICollectionView translationView;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private string provider;

    /// <summary>Перерисовывать выбранные спецификации в чертёжные виды линиями и текстом.</summary>
    [ObservableProperty]
    private bool redrawAsLines = true;

    public SchedulesWindowViewModel(
        IReadOnlyList<ScheduleRowViewModel> rows,
        IReadOnlyList<TranslationRowViewModel> translations,
        TranslationSettings settings)
    {
        if (rows is null)
        {
            throw new ArgumentNullException(nameof(rows));
        }

        if (translations is null)
        {
            throw new ArgumentNullException(nameof(translations));
        }

        Rows = new ObservableCollection<ScheduleRowViewModel>(rows);
        Translations = new ObservableCollection<TranslationRowViewModel>(translations);
        provider = settings.Provider;

        scheduleView = new CollectionViewSource { Source = Rows }.View;
        scheduleView.Filter = MatchesSchedule;

        translationView = new CollectionViewSource { Source = Translations }.View;
        translationView.Filter = MatchesTranslation;
    }

    public ObservableCollection<ScheduleRowViewModel> Rows { get; }

    public ObservableCollection<TranslationRowViewModel> Translations { get; }

    public ICollectionView View
    {
        get { return scheduleView; }
    }

    public ICollectionView TranslationsView
    {
        get { return translationView; }
    }

    public string TranslationsSummary
    {
        get
        {
            var untranslated = Translations.Count(row => !row.HasTranslation);

            return $"Строк {Translations.Count}, без перевода {untranslated}";
        }
    }

    public IReadOnlyList<string> Providers { get; } = ["DeepL", "Google"];

    public IReadOnlyList<ScheduleRowViewModel> SelectedRows
    {
        get { return Rows.Where(row => row.IsSelected).ToList(); }
    }

    public string Summary
    {
        get
        {
            var selected = Rows.Count(row => row.IsSelected);
            var untranslated = Rows.Where(row => row.IsSelected).Sum(row => row.UntranslatedCount);

            return $"Выбрано {selected} из {Rows.Count}, строк без перевода {untranslated}";
        }
    }

    /// <summary>
    ///     Отмечает то, что видно после поиска: иначе галочки встают и на скрытых строках, а пользователь
    ///     этого не видит.
    /// </summary>
    [RelayCommand]
    private void SelectAll()
    {
        SetSelection(true);
    }

    [RelayCommand]
    private void SelectNone()
    {
        SetSelection(false);
    }

    /// <summary>
    ///     Распространяет галочку на всё выделение: пользователь отмечает диапазон строк и переключает одну.
    /// </summary>
    public void ApplySelectionTo(IEnumerable<ScheduleRowViewModel> rows, bool isSelected)
    {
        foreach (var row in rows)
        {
            row.IsSelected = isSelected;
        }

        OnPropertyChanged(nameof(Summary));
    }

    private void SetSelection(bool isSelected)
    {
        foreach (var row in scheduleView.Cast<ScheduleRowViewModel>().ToList())
        {
            row.IsSelected = isSelected;
        }

        OnPropertyChanged(nameof(Summary));
    }

    private bool MatchesSchedule(object item)
    {
        if (SearchText.Length is 0)
        {
            return true;
        }

        if (item is not ScheduleRowViewModel row)
        {
            return false;
        }

        return Contains(row.ScheduleName) || Contains(row.Sheets);
    }

    private bool MatchesTranslation(object item)
    {
        if (SearchText.Length is 0)
        {
            return true;
        }

        if (item is not TranslationRowViewModel row)
        {
            return false;
        }

        return Contains(row.Readable) || Contains(row.Translation) || Contains(row.Schedules);
    }

    private bool Contains(string text)
    {
        return text.IndexOf(SearchText, StringComparison.CurrentCultureIgnoreCase) >= 0;
    }

    /// <summary>Поиск один на окно и фильтрует обе таблицы, чтобы не искать заново при смене вкладки.</summary>
    partial void OnSearchTextChanged(string value)
    {
        scheduleView.Refresh();
        translationView.Refresh();
    }
}
