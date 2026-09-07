using System;
using System.Globalization;

using dosymep.SimpleServices;

using Juru.RevitPlugins.ClashNavigator.Models;

namespace Juru.RevitPlugins.ClashNavigator.Services;
/// <summary>
/// Язык интерфейса плагина: тот, что выбрал пользователь, а не тот, на котором запущен Ревит.
/// </summary>
/// <remarks>
/// На <see cref="ILanguageService.HostLanguage" /> завязано всё: <c>UseWpfLocalization</c> берёт
/// отсюда культуру при создании службы локализации, а <c>WpfLocalizationBehavior</c> - при
/// открытии каждого окна, чтобы подставить строки в разметку. Поэтому подменяется именно этот
/// сервис, а не что-то поверх него: иначе модели представления говорили бы на одном языке,
/// а надписи в разметке - на другом.
/// </remarks>
internal sealed class PluginLanguageService : ILanguageService {
    public const string Russian = "ru-RU";
    public const string English = "en-US";

    private readonly SettingsConfig _settingsConfig;

    public PluginLanguageService(SettingsConfig settingsConfig) {
        _settingsConfig = settingsConfig ?? throw new ArgumentNullException(nameof(settingsConfig));
    }

    public event Action<CultureInfo> LanguageChanged;

    /// <summary>
    /// Пока язык не выбран, берётся русский: плагин писался на нём, и его словарь полный.
    /// </summary>
    public CultureInfo HostLanguage => GetCulture(_settingsConfig.Language);

    /// <summary>
    /// Язык ещё ни разу не выбирали - значит плагин открыли впервые и стоит спросить.
    /// </summary>
    public bool IsLanguageChosen => !string.IsNullOrEmpty(_settingsConfig.Language);

    /// <summary>
    /// Запоминает выбранный язык и сообщает о смене уже открытым окнам.
    /// </summary>
    public void SetLanguage(string language) {
        if(_settingsConfig.Language == language) {
            return;
        }
        _settingsConfig.Language = language;
        _settingsConfig.SaveProjectConfig();
        LanguageChanged?.Invoke(GetCulture(language));
    }

    public static CultureInfo GetCulture(string language) {
        return CultureInfo.GetCultureInfo(string.IsNullOrEmpty(language) ? Russian : language);
    }
}
