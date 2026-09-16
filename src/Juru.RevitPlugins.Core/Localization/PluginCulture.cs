using System.Globalization;

using Autodesk.Revit.ApplicationServices;

using Juru.RevitPlugins.Core.ProjectConfigs;

namespace Juru.RevitPlugins.Core.Localization;

/// <summary>
/// Язык интерфейса плагина: выбранный в настройках, а если не выбран - язык запущенного Ревита.
/// </summary>
public static class PluginCulture {
    public const string Russian = "ru-RU";
    public const string English = "en-US";

    /// <summary>
    /// Культура для окон плагина. Выбор в настройках главнее языка Ревита: пользователь,
    /// переключивший плагин на другой язык, сделал это зная, на чём у него запущен Ревит.
    /// </summary>
    /// <param name="config">Настройки плагина, откуда берётся выбор пользователя.</param>
    /// <param name="revitLanguage">Язык интерфейса запущенного Ревита.</param>
    public static CultureInfo Resolve(ProjectConfig config, LanguageType revitLanguage) {
        return string.IsNullOrEmpty(config.Language)
            ? FromRevitLanguage(revitLanguage)
            : GetCulture(config.Language);
    }

    /// <summary>
    /// Подбирает культуру плагина под язык Ревита.
    /// </summary>
    /// <remarks>
    /// Словарей у плагинов два (ru-RU и en-US), а языков у Ревита полтора десятка, поэтому всё,
    /// что не русский, сводится к английскому: немецкому или японскому пользователю он ближе
    /// русского, а ключи, которых в английском словаре нет, всё равно покажут русский текст - он
    /// подмешан запасным. <see cref="LanguageType.Unknown" /> - случай, когда Ревит язык не
    /// назвал; тогда берётся русский, язык организации, на котором плагины и пишутся.
    /// </remarks>
    public static CultureInfo FromRevitLanguage(LanguageType revitLanguage) {
        return revitLanguage switch {
            LanguageType.Russian => GetCulture(Russian),
            LanguageType.Unknown => GetCulture(Russian),
            _ => GetCulture(English)
        };
    }

    /// <summary>
    /// Культура по имени. Пустое имя означает русский: плагины писались на нём,
    /// и их словарь единственный полный.
    /// </summary>
    public static CultureInfo GetCulture(string language) {
        return CultureInfo.GetCultureInfo(string.IsNullOrEmpty(language) ? Russian : language);
    }
}
