using System.ComponentModel.DataAnnotations;
using System.Reflection;

using dosymep.SimpleServices;

using Juru.RevitPlugins.ClashNavigator.Models.Clashes;

namespace Juru.RevitPlugins.ClashNavigator.Models.Extensions;

internal static class ClashStatusExtensions {
    /// <summary>
    /// Возвращает отображаемое имя статуса из атрибута <see cref="DisplayAttribute"/>.
    /// Не зависит от файла локализации плагина.
    /// </summary>
    public static string GetDisplayName(this ClashStatus status) {
        var member = typeof(ClashStatus).GetMember(status.ToString())[0];
        var display = member.GetCustomAttribute<DisplayAttribute>();
        return display?.Name ?? status.ToString();
    }

    /// <summary>
    /// Возвращает имя статуса на языке интерфейса.
    /// </summary>
    /// <param name="status">Статус коллизии.</param>
    /// <param name="localization">Служба локализации.</param>
    /// <returns>
    /// Строка словаря по ключу <c>ClashStatus.{статус}</c>, а если её там нет -
    /// подпись из <see cref="DisplayAttribute"/>, то есть русское имя статуса.
    /// </returns>
    public static string GetDisplayName(this ClashStatus status, ILocalizationService localization) {
        string name = localization?.GetLocalizedString("ClashStatus." + status);
        return string.IsNullOrEmpty(name) ? status.GetDisplayName() : name;
    }
}
