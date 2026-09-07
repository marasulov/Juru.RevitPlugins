using dosymep.SimpleServices;

using Juru.RevitPlugins.ClashNavigator.Models;
using Juru.RevitPlugins.ClashNavigator.Views;

using Ninject;

namespace Juru.RevitPlugins.ClashNavigator.Services;
/// <summary>
/// Подключение языка интерфейса плагина к контейнеру.
/// </summary>
internal static class PluginLanguageExtensions {
    /// <summary>
    /// Привязывает <see cref="ILanguageService" /> к языку, выбранному пользователем, и спрашивает
    /// этот язык, если его ещё не выбирали. Вызывать вместо <c>UseWpfWindowsLanguage()</c>: тот
    /// отдаёт язык Ревита, а плагин должен говорить на языке, который выбрали в его настройках.
    /// </summary>
    /// <remarks>
    /// <see cref="SettingsConfig" /> должен быть привязан к моменту вызова, а сам вызов - стоять
    /// до открытия окон: язык спрашивается один раз и попадает в службу локализации при её
    /// создании.
    /// </remarks>
    public static IKernel UsePluginLanguage(this IKernel kernel) {
        var languageService = new PluginLanguageService(kernel.Get<SettingsConfig>());
        if(!languageService.IsLanguageChosen) {
            string language = LanguagePromptWindow.Ask();
            if(language != null) {
                languageService.SetLanguage(language);
            }
        }

        kernel.Bind<ILanguageService>()
            .ToConstant(languageService);
        return kernel;
    }
}
