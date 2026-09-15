using System.Globalization;

using Autodesk.Revit.ApplicationServices;

namespace Juru.RevitPlugins.ClashNavigator.Services;
/// <summary>
/// Язык, на котором запущен Ревит, в виде культуры плагина.
/// </summary>
internal static class RevitLanguage {
    /// <summary>
    /// Подбирает культуру плагина под язык Ревита.
    /// </summary>
    /// <param name="language">Язык интерфейса Ревита.</param>
    /// <returns>
    /// <see cref="PluginLanguageService.Russian" /> для русского Ревита и для
    /// <see cref="LanguageType.Unknown" />, иначе <see cref="PluginLanguageService.English" />.
    /// </returns>
    /// <remarks>
    /// Словарей у плагина два, а языков у Ревита полтора десятка, поэтому всё, что не русский,
    /// сводится к английскому: немецкому или японскому пользователю он ближе русского, а ключи,
    /// которых в английском словаре нет, всё равно покажут русский текст - он подмешан запасным.
    /// <see cref="LanguageType.Unknown" /> - случай, когда Ревит язык не назвал; тогда берётся
    /// русский, язык организации, на котором плагин и писался.
    /// </remarks>
    public static CultureInfo GetCulture(LanguageType language) {
        return language switch {
            LanguageType.Russian => PluginLanguageService.GetCulture(PluginLanguageService.Russian),
            LanguageType.Unknown => PluginLanguageService.GetCulture(PluginLanguageService.Russian),
            _ => PluginLanguageService.GetCulture(PluginLanguageService.English)
        };
    }
}
