using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

using dosymep.SimpleServices;
using dosymep.WPF.Commands;
using dosymep.WPF.ViewModels;

using Juru.RevitPlugins.ClashNavigator.Models;
using Juru.RevitPlugins.ClashNavigator.Services;

namespace Juru.RevitPlugins.ClashNavigator.ViewModels.Settings;
internal class SettingsViewModel : BaseViewModel {
    private readonly RevitRepository _revitRepository;
    private readonly SettingsConfig _config;
    private readonly ILocalizationService _localizationService;
    private string _errorText;
    private int _sectionBoxOffset;
    private const int _maxSectionBoxOffset = 10000;
    private bool _applySectionBoxSettings;
    private bool _highlightClashElements;
    private bool _halftoneOtherElements;
    private string _clashServerUrl;
    private LanguageItem _selectedLanguage;

    public SettingsViewModel(RevitRepository revitRepository, SettingsConfig config, ILocalizationService localizationService) {
        _revitRepository = revitRepository ?? throw new ArgumentNullException(nameof(revitRepository));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));

        LoadViewCommand = RelayCommand.Create(LoadView);
        AcceptViewCommand = RelayCommand.Create(AcceptView, CanAcceptView);
    }


    /// <summary>
    /// Языки интерфейса. Подписи взяты из словаря локализации, и названия самих языков там
    /// одинаковы для обеих культур: "Русский" остаётся "Русский" и в английском интерфейсе -
    /// так принято у языковых переключателей, чтобы свой язык был узнаваем при любом текущем.
    /// </summary>
    /// <remarks>
    /// Первый пункт - пустая строка, то есть "язык не выбран": с ней плагин следует за языком
    /// Ревита. Без такого пункта вернуться к нему после первого же переключения было бы нечем.
    /// </remarks>
    public IReadOnlyList<LanguageItem> Languages { get; } = [
        new(string.Empty, "Language.Auto"),
        new(PluginLanguageService.Russian, "Language.Russian"),
        new(PluginLanguageService.English, "Language.English")
    ];

    public LanguageItem SelectedLanguage {
        get => _selectedLanguage;
        set => RaiseAndSetIfChanged(ref _selectedLanguage, value);
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

    public bool HighlightClashElements {
        get => _highlightClashElements;
        set => RaiseAndSetIfChanged(ref _highlightClashElements, value);
    }

    public bool HalftoneOtherElements {
        get => _halftoneOtherElements;
        set => RaiseAndSetIfChanged(ref _halftoneOtherElements, value);
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
        foreach(var language in Languages) {
            language.Title = _localizationService.GetLocalizedString(language.TitleKey);
        }
        SelectedLanguage = Languages.FirstOrDefault(item => item.Language == _config.Language)
            ?? Languages[0];
        SectionBoxOffset = _config.SectionBoxOffset;
        ApplySectionBoxSettings = _config.ApplySectionBoxSettings;
        HighlightClashElements = _config.HighlightClashElements;
        HalftoneOtherElements = _config.HalftoneOtherElements;
        ClashServerUrl = _config.ClashServerUrl;
    }

    private void SaveConfig() {
        // Язык применится при следующем открытии окон: у уже открытых разметка уже разобрана,
        // а строки в моделях представления прочитаны один раз при их создании.
        // ?. с ?? тут нельзя: у пункта "как в Ревите" язык - пустая строка, и ?? пропустил бы
        // её мимо, оставив в настройках прежний язык.
        _config.Language = SelectedLanguage is null ? _config.Language : SelectedLanguage.Language;
        _config.SectionBoxOffset = SectionBoxOffset;
        _config.ApplySectionBoxSettings = ApplySectionBoxSettings;
        _config.HighlightClashElements = HighlightClashElements;
        _config.HalftoneOtherElements = HalftoneOtherElements;
        _config.ClashServerUrl = ClashServerUrl?.Trim() ?? string.Empty;
        _config.SaveProjectConfig();
    }
}

/// <summary>
/// Язык интерфейса в выпадающем списке настроек.
/// </summary>
internal sealed class LanguageItem : BaseViewModel {
    private string _title;

    public LanguageItem(string language, string titleKey) {
        Language = language;
        TitleKey = titleKey;
    }

    public string Language { get; }

    public string TitleKey { get; }

    public string Title {
        get => _title;
        set => RaiseAndSetIfChanged(ref _title, value);
    }
}
