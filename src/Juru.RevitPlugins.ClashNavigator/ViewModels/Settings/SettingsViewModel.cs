using System;
using System.Windows.Input;

using dosymep.SimpleServices;
using dosymep.WPF.Commands;
using dosymep.WPF.ViewModels;

using Juru.RevitPlugins.ClashNavigator.Models;

namespace Juru.RevitPlugins.ClashNavigator.ViewModels.Settings;
internal class SettingsViewModel : BaseViewModel {
    private readonly RevitRepository _revitRepository;
    private readonly SettingsConfig _config;
    private readonly ILocalizationService _localizationService;
    private string _errorText;
    private int _sectionBoxOffset;
    private const int _maxSectionBoxOffset = 10000;
    private bool _applySectionBoxSettings;
    private string _clashServerUrl;

    public SettingsViewModel(RevitRepository revitRepository, SettingsConfig config, ILocalizationService localizationService) {
        _revitRepository = revitRepository ?? throw new ArgumentNullException(nameof(revitRepository));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));

        LoadViewCommand = RelayCommand.Create(LoadView);
        AcceptViewCommand = RelayCommand.Create(AcceptView, CanAcceptView);
    }


    public ICommand LoadViewCommand { get; }

    public ICommand AcceptViewCommand { get; }

    public string ErrorText {
        get => _errorText;
        set => RaiseAndSetIfChanged(ref _errorText, value);
    }

    public int SectionBoxOffset {
        get => _sectionBoxOffset;
        set => RaiseAndSetIfChanged(ref _sectionBoxOffset, value);
    }

    public bool ApplySectionBoxSettings {
        get => _applySectionBoxSettings;
        set => RaiseAndSetIfChanged(ref _applySectionBoxSettings, value);
    }

    /// <summary>
    /// Адрес сервера коллизий для загрузки отчётов по кнопке "Загрузить с сервера" (например, http://localhost:5000)
    /// </summary>
    public string ClashServerUrl {
        get => _clashServerUrl;
        set => RaiseAndSetIfChanged(ref _clashServerUrl, value);
    }


    private void LoadView() {
        LoadConfig();
    }

    private void AcceptView() {
        SaveConfig();
    }

    private bool CanAcceptView() {
        if(SectionBoxOffset < 0) {
            ErrorText = _localizationService.GetLocalizedString(
                "SettingsWindow.Validation.OffsetLessThanZero");
            return false;
        }
        if(SectionBoxOffset > _maxSectionBoxOffset) {
            ErrorText = _localizationService.GetLocalizedString(
                "SettingsWindow.Validation.OffsetGreaterThan", _maxSectionBoxOffset);
            return false;
        }

        ErrorText = null;
        return true;
    }

    private void LoadConfig() {
        SectionBoxOffset = _config.SectionBoxOffset;
        ApplySectionBoxSettings = _config.ApplySectionBoxSettings;
        ClashServerUrl = _config.ClashServerUrl;
    }

    private void SaveConfig() {
        _config.SectionBoxOffset = SectionBoxOffset;
        _config.ApplySectionBoxSettings = ApplySectionBoxSettings;
        _config.ClashServerUrl = ClashServerUrl?.Trim() ?? string.Empty;
        _config.SaveProjectConfig();
    }
}
