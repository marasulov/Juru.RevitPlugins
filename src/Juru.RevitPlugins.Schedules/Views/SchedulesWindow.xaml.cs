using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

using Juru.RevitPlugins.Core.Resources;

using Juru.RevitPlugins.Schedules.ViewModels;

using Wpf.Ui.Appearance;

using Juru.RevitPlugins.Schedules.Localization;

namespace Juru.RevitPlugins.Schedules.Views;

/// <summary>
///     Выбор спецификаций для английского комплекта.
/// </summary>
public partial class SchedulesWindow : Window
{
    public SchedulesWindow(SchedulesWindowViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;

        InitializeComponent();

        // Обычное Window, а не FluentWindow: FluentWindow поднимает тему WPF-UI себе сам, а
        // простое окно обязано вызвать это само - иначе строки таблиц и кнопки остаются
        // нестилизованными, то есть белыми поверх тёмного окна.
        ApplicationThemeManager.Apply(this);

        CheckBoxBrushes.OverrideCheckedStates(this);
        AccentButtonBrushes.Override(this);

        // Фон и цвет текста привязываются только здесь, а не в разметке: ui:ThemeResource
        // разворачивается при разборе XAML, то есть до того, как Apply положит словари темы в окно,
        // и ключ тогда не находится. Незакрашенный фон окна виден как чёрные полосы при растягивании.
        SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");
        SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
    }

    public SchedulesWindowViewModel ViewModel { get; }

    /// <summary>
    ///     Переключает галочку строки. Щелчок гасится здесь и до таблицы не доходит.
    /// </summary>
    /// <remarks>
    ///     Переключение делается вручную, а <c>IsChecked</c> привязан односторонне: иначе
    ///     <c>ToggleButton</c> переключит значение сам, и на один щелчок придётся два изменения.
    ///     Гашение события заодно не отдаёт щелчок таблице, чтобы он не менял выделение строк.
    /// </remarks>
    private void CheckBoxCell_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (sender is not CheckBox { DataContext: ISelectableRow row })
        {
            return;
        }

        row.IsSelected = !row.IsSelected;
        eventArgs.Handled = true;
    }

    /// <summary>
    ///     Открывает настройки перевода. Окно модальное над главным: настройки меняют то, как пойдёт
    ///     сборка, и запускать её, пока их правят, нельзя.
    /// </summary>
    /// <summary>
    ///     Сообщает вьюмодели, какая вкладка открыта: кнопки выбора, перевода и показа одни на окно
    ///     и работают по ней. Какая вкладка активна, знает разметка, а не вьюмодель.
    /// </summary>
    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (sender is TabControl { SelectedItem: TabItem { Tag: string key } })
        {
            ViewModel.UseSurface(key);
        }
    }

    private void ButtonSettings_Click(object sender, RoutedEventArgs eventArgs)
    {
        var settings = new SettingsViewModel(ViewModel.Config);
        var window = new SettingsWindow(settings) { Owner = this };

        if (window.ShowDialog() is not true)
        {
            return;
        }

        var updated = settings.Result;
        var failure = updated.TrySave();

        ViewModel.ApplySettings(updated);

        ViewModel.StatusNote = failure is null
            ? Text.StatusSettingsSaved
            : string.Format(Text.SettingsSaveFailed, failure);
    }

    private void ButtonClose_Click(object sender, RoutedEventArgs eventArgs)
    {
        Close();
    }
}
