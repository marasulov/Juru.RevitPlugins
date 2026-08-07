using System.ComponentModel.DataAnnotations;
using System.Reflection;

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
}
