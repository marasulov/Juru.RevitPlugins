using Autodesk.Revit.UI;

using dosymep.SimpleServices;

using Juru.RevitPlugins.ClashNavigator.Models;

using Ninject;

namespace Juru.RevitPlugins.ClashNavigator.Services;
/// <summary>
/// Подключение языка интерфейса плагина к контейнеру.
/// </summary>
internal static class PluginLanguageExtensions {
    /// <summary>
    /// Привязывает <see cref="ILanguageService" /> к языку запущенного Ревита, а если язык выбран
    /// в настройках плагина - к нему. Вызывать вместо <c>UseWpfWindowsLanguage()</c>: тот отдаёт
    /// язык Windows, который с языком Ревита совпадает не всегда.
    /// </summary>
    /// <remarks>
    /// <see cref="UIApplication" /> и <see cref="SettingsConfig" /> должны быть привязаны к моменту
    /// вызова, а сам вызов - стоять до открытия окон: язык попадает в службу локализации при её
    /// создании.
    /// </remarks>
    public static IKernel UsePluginLanguage(this IKernel kernel) {
        var revitLanguage = RevitLanguage.GetCulture(
            kernel.Get<UIApplication>().Application.Language);

        kernel.Bind<ILanguageService>()
            .ToConstant(new PluginLanguageService(kernel.Get<SettingsConfig>(), revitLanguage));
        return kernel;
    }
}
