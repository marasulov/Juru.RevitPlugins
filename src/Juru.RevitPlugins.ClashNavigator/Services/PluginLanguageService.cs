using System;
using System.Globalization;

using dosymep.SimpleServices;

using Juru.RevitPlugins.ClashNavigator.Models;

namespace Juru.RevitPlugins.ClashNavigator.Services;
/// <summary>
/// Язык интерфейса плагина: тот, на котором запущен Ревит, либо выбранный в настройках плагина.
/// </summary>
/// <remarks>
/// На <see cref="ILanguageService.HostLanguage" /> завязано всё: <c>UseWpfLocalization</c> берёт
/// отсюда культуру при создании службы локализации, а <c>WpfLocalizationBehavior</c> - при
/// открытии каждого окна, чтобы подставить строки в разметку. Поэтому подменяется именно этот
/// сервис, а не что-то поверх него: иначе модели представления говорили бы на одном языке,
/// а надписи в разметке - на другом.
/// <para>
/// Готовый <c>UseWpfWindowsLanguage()</c> для этого не годится: он отдаёт язык Windows, а не
/// Ревита, и на русской Windows с английским Ревитом ответил бы русским.
/// </para>
/// </remarks>
internal sealed class PluginLanguageService : ILanguageService {
    public const string Russian = "ru-RU";
    public const string English = "en-US";

    private readonly SettingsConfig _settingsConfig;
    private readonly CultureInfo _revitLanguage;

    /// <param name="settingsConfig">Настройки плагина, где хранится выбранный язык.</param>
    /// <param name="revitLanguage">Культура, отвечающая языку запущенного Ревита.</param>
    public PluginLanguageService(SettingsConfig settingsConfig, CultureInfo revitLanguage) {
        _settingsConfig = settingsConfig ?? throw new ArgumentNullException(nameof(settingsConfig));
        _revitLanguage = revitLanguage ?? throw new ArgumentNullException(nameof(revitLanguage));
    }

    public event Action<CultureInfo> LanguageChanged;

    /// <summary>
    /// Язык из настроек плагина, а пока он там не выбран - язык Ревита.
    /// </summary>
    /// <remarks>
    /// Выбор в настройках главнее: пользователь, который переключил плагин на другой язык,
    /// сделал это зная, на чём у него запущен Ревит.
    /// </remarks>
    public CultureInfo HostLanguage => string.IsNullOrEmpty(_settingsConfig.Language)
        ? _revitLanguage
        : GetCulture(_settingsConfig.Language);

    /// <summary>
    /// Запоминает выбранный язык и сообщает о смене уже открытым окнам.
    /// </summary>
    /// <param name="language">
    /// Имя культуры, либо пустая строка, чтобы снова следовать за языком Ревита.
    /// </param>
    public void SetLanguage(string language) {
        if(_settingsConfig.Language == language) {
            return;
        }
        _settingsConfig.Language = language;
        _settingsConfig.SaveProjectConfig();
        // Именно HostLanguage, а не культура из аргумента: на пустой строке окнам нужен язык
        // Ревита, а не запасной русский, который вернула бы GetCulture.
        LanguageChanged?.Invoke(HostLanguage);
    }

    /// <summary>
    /// Культура по имени. Пустое имя означает русский: плагин писался на нём,
    /// и его словарь единственный полный.
    /// </summary>
    public static CultureInfo GetCulture(string language) {
        return CultureInfo.GetCultureInfo(string.IsNullOrEmpty(language) ? Russian : language);
    }
}
