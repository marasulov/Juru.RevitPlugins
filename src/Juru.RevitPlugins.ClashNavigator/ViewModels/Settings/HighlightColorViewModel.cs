using System;
using System.Windows.Input;

using Autodesk.Revit.UI;

using dosymep.WPF.Commands;
using dosymep.WPF.ViewModels;

using Juru.RevitPlugins.ClashNavigator.Models;

using MediaColor = System.Windows.Media.Color;

namespace Juru.RevitPlugins.ClashNavigator.ViewModels.Settings;

/// <summary>
/// Цвет и прозрачность подсветки одного элемента коллизии в окне настроек.
/// </summary>
internal sealed class HighlightColorViewModel : BaseViewModel {
    private readonly Func<HighlightColorSettings> _getDefault;
    private MediaColor _color;
    private int _transparency;

    /// <param name="getDefault">Значение по умолчанию - для кнопки сброса.</param>
    public HighlightColorViewModel(Func<HighlightColorSettings> getDefault) {
        _getDefault = getDefault ?? throw new ArgumentNullException(nameof(getDefault));
        PickColorCommand = RelayCommand.Create(PickColor);
        ResetCommand = RelayCommand.Create(Reset);
    }

    public ICommand PickColorCommand { get; }

    public ICommand ResetCommand { get; }

    public MediaColor Color {
        get => _color;
        set => RaiseAndSetIfChanged(ref _color, value);
    }

    /// <summary>
    /// Прозрачность граней, %: 0 - непрозрачные, 100 - невидимые.
    /// </summary>
    public int Transparency {
        get => _transparency;
        set => RaiseAndSetIfChanged(ref _transparency, Math.Max(0, Math.Min(100, value)));
    }

    public void Load(HighlightColorSettings settings) {
        settings ??= _getDefault();
        Color = MediaColor.FromRgb(settings.Red, settings.Green, settings.Blue);
        Transparency = settings.Transparency;
    }

    public HighlightColorSettings GetSettings() {
        return new HighlightColorSettings {
            Red = Color.R,
            Green = Color.G,
            Blue = Color.B,
            Transparency = Transparency
        };
    }

    // Штатный диалог выбора цвета Ревита: тот же, что в переопределениях графики,
    // и не тянет в плагин отдельную библиотеку палитры.
    private void PickColor() {
        using var dialog = new ColorSelectionDialog {
            OriginalColor = new Autodesk.Revit.DB.Color(Color.R, Color.G, Color.B)
        };
        if(dialog.Show() == ItemSelectionDialogResult.Confirmed) {
            var color = dialog.SelectedColor;
            Color = MediaColor.FromRgb(color.Red, color.Green, color.Blue);
        }
    }

    private void Reset() {
        Load(_getDefault());
    }
}
