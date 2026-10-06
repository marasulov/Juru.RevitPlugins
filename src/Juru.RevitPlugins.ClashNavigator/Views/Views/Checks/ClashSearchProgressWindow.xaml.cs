using System;
using System.Windows;

using dosymep.SimpleServices;

namespace Juru.RevitPlugins.ClashNavigator.Views.Checks;
/// <summary>
/// Окно хода поиска коллизий с кнопкой отмены.
/// </summary>
internal partial class ClashSearchProgressWindow : Window {
    private readonly ILocalizationService _localizationService;

    public ClashSearchProgressWindow(ILocalizationService localizationService) {
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        InitializeComponent();

        Title = localizationService.GetLocalizedString("ChecksWindow.Progress.Title");
        StageText.Text = localizationService.GetLocalizedString("ChecksWindow.Progress.Preparing");
        CancelButton.Content = localizationService.GetLocalizedString("Common.Cancel");
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
        StageText.Text = $"{stage} [{value}/{(int) Bar.Maximum}]";
        Bar.Value = Math.Min(value, Bar.Maximum);
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) {
        Cancelled = true;
        CancelButton.IsEnabled = false;
        StageText.Text = _localizationService.GetLocalizedString("ChecksWindow.Progress.Cancelling");
    }
}
