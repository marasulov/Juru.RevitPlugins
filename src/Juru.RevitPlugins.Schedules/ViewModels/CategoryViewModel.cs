using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Juru.RevitPlugins.Schedules.Models;
using Juru.RevitPlugins.Schedules.Services.Translation;

using Juru.RevitPlugins.Schedules.Localization;

namespace Juru.RevitPlugins.Schedules.ViewModels;

/// <summary>
///     Вкладка одной категории модели: список её текстов, их переводы и то, что с ними сделают по кнопке.
///     Одна и та же модель обслуживает любую категорию — они различаются только сбором строк.
/// </summary>
public sealed partial class CategoryViewModel : ObservableObject
{
    private readonly ICollectionView view;
    private readonly Func<TranslationSettings> settings;
    private readonly Action<ElementText>? show;

    private Func<string, bool> matches = _ => true;

    [ObservableProperty]
    private string note = string.Empty;

    /// <param name="emptyNote">
    ///     Что показать вместо таблицы, если строк нет. Пустая таблица без объяснения выглядит
    ///     сломанной: непонятно, в модели такого текста нет или плагин его не нашёл.
    /// </param>
    /// <param name="show">
    ///     Показать элемент в модели. Задаёт команда: вкладка не знает ни документа, ни вида.
    /// </param>
    public CategoryViewModel(
        string key,
        string name,
        IReadOnlyList<ElementTextRowViewModel> rows,
        string emptyNote,
        Func<TranslationSettings> settings,
        Action<ElementText>? show = null)
    {
        Key = key;
        Name = name;
        EmptyNote = emptyNote;
        this.settings = settings;
        this.show = show;

        Rows = new ObservableCollection<ElementTextRowViewModel>(rows);

        view = new CollectionViewSource { Source = Rows }.View;
        view.Filter = Matches;

        foreach (var row in Rows)
        {
            row.PropertyChanged += OnRowChanged;
        }
    }

    /// <summary>Устойчивое имя для кода: <see cref="Name" /> переводится.</summary>
    public string Key { get; }

    public string Name { get; }

    /// <summary>Текст вместо таблицы, когда переводить в этой категории нечего.</summary>
    public string EmptyNote { get; }

    public ObservableCollection<ElementTextRowViewModel> Rows { get; }

    public bool HasRows
    {
        get { return Rows.Count > 0; }
    }

    /// <summary>Переводить нечего: вместо таблицы окно показывает <see cref="EmptyNote" />.</summary>
    public bool IsEmpty
    {
        get { return Rows.Count is 0; }
    }

    public ICollectionView View
    {
        get { return view; }
    }

    public string Summary
    {
        get
        {
            var selected = Rows.Count(row => row.IsSelected);
            var missing = Rows.Count(row => !row.HasTranslation);

            return string.Format(Text.SummaryCategory, Rows.Count, selected, missing);
        }
    }

    /// <summary>Строка под курсором таблицы: по ней работает «Показать».</summary>
    public ElementTextRowViewModel? Current
    {
        get { return view.CurrentItem as ElementTextRowViewModel; }
    }

    /// <summary>Отмеченные строки с непустым переводом и отмеченные переведённые — их откатят.</summary>
    public IReadOnlyList<ElementTextRowViewModel> SelectedRows
    {
        get { return Rows.Where(row => row.IsSelected).ToList(); }
    }

    /// <summary>
    ///     Заполняет вкладку прочитанным. Строки приходят после открытия окна: чтение модели идёт
    ///     под уже показанным окном, чтобы Ревит не выглядел зависшим.
    /// </summary>
    public void Fill(IReadOnlyList<ElementTextRowViewModel> rows)
    {
        foreach (var row in Rows)
        {
            row.PropertyChanged -= OnRowChanged;
        }

        Rows.Clear();

        foreach (var row in rows)
        {
            row.PropertyChanged += OnRowChanged;
            Rows.Add(row);
        }

        view.Refresh();

        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Summary));
    }

    /// <summary>Поиск один на окно, поэтому условие задаёт окно, а фильтрует вкладка.</summary>
    public void ApplyFilter(Func<string, bool> condition)
    {
        matches = condition;
        view.Refresh();
    }

    /// <summary>Сообщает строкам, что режим записи или разделитель изменились.</summary>
    public void RefreshResults()
    {
        foreach (var row in Rows)
        {
            row.RefreshResult();
        }
    }

    /// <summary>
    ///     Показывает в модели элемент выделенной строки. Работает по выделению таблицы, а не по
    ///     галочкам: галочками отмечают то, что переведут, а показать нужно одно конкретное место.
    /// </summary>
    [RelayCommand]
    private void ShowSelected()
    {
        if (show is null)
        {
            return;
        }

        var row = Current;

        if (row is null)
        {
            Note = Text.StatusPickElementRow;
            return;
        }

        Note = string.Empty;
        show(row.Text);
    }

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
    ///     Переводит машиной только пустые переводы среди видимых строк: заполненное человеком или
    ///     подставленное из глоссария не перетирается.
    /// </summary>
    [RelayCommand]
    private async Task TranslateMissingAsync()
    {
        var pending = view
            .Cast<ElementTextRowViewModel>()
            .Where(row => !row.HasTranslation)
            .ToList();

        if (pending.Count is 0)
        {
            Note = Text.NoteNothingToTranslate;
            return;
        }

        var current = settings();
        var translator = TranslatorFactory.TryCreate(current, out var reason);

        if (translator is null)
        {
            Note = reason ?? Text.NoteServiceNotConfigured;
            return;
        }

        try
        {
            Note = await TranslateAsync(translator, current, pending).ConfigureAwait(true);
        }
        finally
        {
            OnPropertyChanged(nameof(Summary));
        }
    }

    /// <summary>
    ///     Один текст переводится один раз: одинаковые названия помещений в модели встречаются десятками,
    ///     и служба перевода тарифицируется по символам.
    /// </summary>
    private static async Task<string> TranslateAsync(
        ITextTranslator translator,
        TranslationSettings settings,
        IReadOnlyList<ElementTextRowViewModel> pending)
    {
        var unique = pending
            .Select(row => row.SourceText)
            .Distinct(StringComparer.CurrentCulture)
            .ToList();

        var translations = new Dictionary<string, string>(StringComparer.CurrentCulture);
        var failures = new List<string>();

        foreach (var batch in Batch(unique, Math.Max(1, settings.BatchSize)))
        {
            try
            {
                var translated = await translator
                    .TranslateAsync(batch, CancellationToken.None)
                    .ConfigureAwait(true);

                for (var index = 0; index < batch.Count; index++)
                {
                    translations[batch[index]] = translated[index].Trim();
                }
            }
            catch (TranslationException exception)
            {
                failures.Add(exception.Message);
            }
        }

        foreach (var row in pending)
        {
            if (translations.TryGetValue(row.SourceText, out var translation) && translation.Length > 0)
            {
                row.Translation = translation;
            }

            // Отмечаем то, что переводили: иначе перевод есть, а «Создать» его не запишет, потому
            // что записывается только отмеченное. Непереведённые тоже отмечены - их видно в отчёте
            // как пропущенные, а не молча теряются.
            row.IsSelected = true;
        }

        var done = pending.Count(row => row.HasTranslation);

        return failures.Count is 0
            ? string.Format(Text.NoteTranslated, done, pending.Count, translator.Name)
            : string.Format(Text.NoteTranslatedWithErrors, done, pending.Count, failures.Count, failures[0]);
    }

    private static IEnumerable<IReadOnlyList<string>> Batch(IReadOnlyList<string> texts, int size)
    {
        for (var start = 0; start < texts.Count; start += size)
        {
            yield return texts.Skip(start).Take(size).ToList();
        }
    }

    private void SetSelection(bool isSelected)
    {
        foreach (var row in view.Cast<ElementTextRowViewModel>().ToList())
        {
            row.IsSelected = isSelected;
        }
    }

    private bool Matches(object item)
    {
        if (item is not ElementTextRowViewModel row)
        {
            return false;
        }

        return matches(row.SourceText) || matches(row.Context) || matches(row.Translation);
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(ElementTextRowViewModel.IsSelected)
            or nameof(ElementTextRowViewModel.Translation))
        {
            OnPropertyChanged(nameof(Summary));
        }
    }
}
