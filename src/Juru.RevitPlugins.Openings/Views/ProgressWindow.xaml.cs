using System;
using System.Windows;

using Juru.RevitPlugins.Core.Localization;

namespace Juru.RevitPlugins.Openings.Views;

/// <summary>
/// Окно хода расчёта с возможностью отмены.
/// </summary>
public partial class ProgressWindow : Window {
    private readonly PluginLocalization _localization;

    public ProgressWindow(PluginLocalization localization) {
        _localization = localization;
        InitializeComponent();
        localization.ApplyTo(this);
    }

    /// <summary>
    /// Пользователь нажал отмену.
    /// </summary>
    public bool Cancelled { get; private set; }

    /// <summary>
    /// Задаёт название этапа и общее число шагов.
    /// </summary>
    public void SetStage(string stage, int maximum) {
        StageText.Text = stage;
        Bar.Maximum = Math.Max(maximum, 1);
        Bar.Value = 0;
    }

    /// <summary>
    /// Обновляет число обработанных шагов.
    /// </summary>
    public void SetProgress(string stage, int value) {
        StageText.Text = $"{stage} [{value}\\{(int) Bar.Maximum}]";
        Bar.Value = Math.Min(value, Bar.Maximum);
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) {
        Cancelled = true;
        CancelButton.IsEnabled = false;
        StageText.Text = _localization.GetString("Progress.Cancelling");
    }
}
