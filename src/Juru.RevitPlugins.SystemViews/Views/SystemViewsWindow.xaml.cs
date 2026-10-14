using System;
using System.Windows;

using Juru.RevitPlugins.Core.Resources;
using Juru.RevitPlugins.SystemViews.ViewModels;

using Wpf.Ui.Appearance;

namespace Juru.RevitPlugins.SystemViews.Views;

/// <summary>
///     Окно «Виды по системам». Модальное: модель читается в контексте Revit API, пока оно открыто.
/// </summary>
public partial class SystemViewsWindow : Window
{
    public SystemViewsWindow(SystemViewsViewModel viewModel)
    {
        DataContext = viewModel;

        InitializeComponent();

        // Простое Window поднимает тему WPF-UI само, как и окна Schedules.
        ApplicationThemeManager.Apply(this);

        CheckBoxBrushes.OverrideCheckedStates(this);
        AccentButtonBrushes.Override(this);

        SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");
        SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");

        viewModel.Accepted += OnAccepted;
        Closed += (_, _) => viewModel.Accepted -= OnAccepted;
    }

    private void OnAccepted(object? sender, EventArgs eventArgs)
    {
        DialogResult = true;
    }
}
