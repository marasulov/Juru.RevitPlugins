using System.Windows.Input;
using MediaColor = System.Windows.Media.Color;

using Autodesk.Revit.UI;

using dosymep.SimpleServices;
using dosymep.WPF.Commands;
using dosymep.WPF.ViewModels;

using Juru.RevitPlugins.ClashNavigator.Models;

namespace Juru.RevitPlugins.ClashNavigator.ViewModels.Settings;
internal class ElementVisibilitySettingsViewModel : BaseViewModel {
    private readonly ILocalizationService _localizationService;
    private MediaColor _color;
    private int _transparency;

    public ElementVisibilitySettingsViewModel(ILocalizationService localizationService) {
        _localizationService = localizationService
            ?? throw new System.ArgumentNullException(nameof(localizationService));
        ColorLabel = _localizationService.GetLocalizedString("ElementVisibilitySettings.ColorLabel");
        TransparencyLabel = _localizationService.GetLocalizedString("ElementVisibilitySettings.TransparencyLabel");

        ResetColorCommand = RelayCommand.Create(ResetColor);
    }


    public ICommand ResetColorCommand { get; }

    public string ColorLabel { get; }

    public string TransparencyLabel { get; }

    public MediaColor Color {
        get => _color;
        set => RaiseAndSetIfChanged(ref _color, value);
    }

    public int Transparency {
        get => _transparency;
        set => RaiseAndSetIfChanged(ref _transparency, value);
    }

    public ElementVisibilitySettings GetSettings() {
        return new ElementVisibilitySettings() {
            Color = _color,
            Transparency = _transparency
        };
    }

    private void ResetColor() {
        using var colorSelectionDialog = new ColorSelectionDialog();

        if(colorSelectionDialog.Show() == ItemSelectionDialogResult.Confirmed) {
            var color = colorSelectionDialog.SelectedColor;
            Color = MediaColor.FromRgb(color.Red, color.Green, color.Blue);
        }
    }
}
