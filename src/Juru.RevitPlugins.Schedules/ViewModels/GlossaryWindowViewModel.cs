using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Data;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Juru.RevitPlugins.Schedules.Localization;
using Juru.RevitPlugins.Schedules.Models;
using Juru.RevitPlugins.Schedules.Services;

namespace Juru.RevitPlugins.Schedules.ViewModels;

/// <summary>
///     Весь словарь переводов: правка вручную и свои строки.
/// </summary>
/// <remarks>
///     Словарь - это тот же CSV, который пишет сборка, и он общий для всех моделей. Отдельное окно,
///     а не вкладка в главном: строки словаря не привязаны ни к модели, ни к области сканирования,
///     и собирать их заново при каждом открытии главного окна незачем - именно эта сборка и делала
///     открытие долгим.
/// </remarks>
public sealed partial class GlossaryWindowViewModel : ObservableObject
{
    private readonly ICollectionView view;

    [ObservableProperty]
    private string searchText = string.Empty;

    public GlossaryWindowViewModel(string path)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));

        Rows = new RowCollection<GlossaryRowViewModel>();
        Rows.Reset(GlossaryFile.ReadAll(path)
            .OrderBy(entry => entry.Readable, StringComparer.CurrentCulture)
            .Select(entry => new GlossaryRowViewModel(entry)));

        view = new CollectionViewSource { Source = Rows }.View;
        view.Filter = Matches;
    }

    public RowCollection<GlossaryRowViewModel> Rows { get; }

    public ICollectionView View
    {
        get { return view; }
    }

    /// <summary>Путь файла словаря: он общий для всех моделей, и знать, какой именно, полезно.</summary>
    public string Path { get; }

    public bool IsEmpty
    {
        get { return Rows.Count is 0; }
    }

    public string Summary
    {
        get
        {
            var translated = Rows.Count(row => row.Translation.Trim().Length > 0);
            var byHand = Rows.Count(row => row.IsNew || row.IsEdited || row.Source is TranslationSource.Human);

            return string.Format(Text.DictionarySummary, Rows.Count, translated, byHand);
        }
    }

    /// <summary>Добавляет пустую строку и показывает её, даже если поиск её бы скрыл.</summary>
    [RelayCommand]
    private void Add()
    {
        SearchText = string.Empty;

        var row = new GlossaryRowViewModel();

        Rows.Add(row);
        view.MoveCurrentTo(row);

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Summary));
    }

    [RelayCommand]
    private void Remove()
    {
        if (view.CurrentItem is not GlossaryRowViewModel row)
        {
            return;
        }

        Rows.Remove(row);

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Summary));
    }

    /// <summary>
    ///     Пишет словарь на диск и возвращает сообщение об итоге.
    /// </summary>
    /// <remarks>
    ///     Строки без исходного текста отбрасываются: это добавленные кнопкой и брошенные пустыми.
    ///     Одинаковые ключи сводятся в один - побеждает тот, у которого перевод есть, иначе при
    ///     подстановке всё равно нашёлся бы только первый.
    /// </remarks>
    public string Save()
    {
        var entries = Rows
            .Select(row => row.ToEntry())
            .Where(entry => entry.Key.Length > 0)
            .GroupBy(entry => entry.Key, StringComparer.CurrentCulture)
            .Select(group => group.FirstOrDefault(entry => entry.Suggestion.Length > 0) ?? group.First())
            .OrderBy(entry => entry.Readable, StringComparer.CurrentCulture)
            .ToList();

        try
        {
            GlossaryFile.Write(entries, Path);

            return string.Format(Text.DictionarySaved, entries.Count);
        }
        catch (IOException failure)
        {
            return string.Format(Text.DictionarySaveFailed, failure.Message);
        }
        catch (UnauthorizedAccessException failure)
        {
            return string.Format(Text.DictionarySaveFailed, failure.Message);
        }
    }

    private bool Matches(object item)
    {
        return item is GlossaryRowViewModel row && row.Matches(SearchText);
    }

    partial void OnSearchTextChanged(string value)
    {
        view.Refresh();
    }
}
