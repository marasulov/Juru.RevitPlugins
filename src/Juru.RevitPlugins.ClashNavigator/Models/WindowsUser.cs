using System;
using System.Security.Principal;

namespace Juru.RevitPlugins.ClashNavigator.Models;

/// <summary>
/// Учётная запись Windows, под которой запущен Ревит. Автора изменения статуса коллизии
/// определяем по ней, а не по имени пользователя Autodesk: в базу нужно писать сотрудника организации.
/// </summary>
internal static class WindowsUser {
    /// <summary>
    /// Полное имя учётной записи в формате DOMAIN\user.
    /// </summary>
    public static string Name { get; } = GetName();

    /// <summary>
    /// Имя учётной записи без домена.
    /// </summary>
    public static string DisplayName { get; } = GetDisplayName(Name);

    private static string GetName() {
        try {
            using var identity = WindowsIdentity.GetCurrent();
            if(!string.IsNullOrWhiteSpace(identity?.Name)) {
                return identity.Name;
            }
        } catch(SystemException) {
            // Токен текущего пользователя может быть недоступен - тогда хватит переменных окружения.
        }

        string domain = Environment.UserDomainName;
        string user = Environment.UserName;
        return string.IsNullOrWhiteSpace(domain) ? user : $"{domain}\\{user}";
    }

    private static string GetDisplayName(string name) {
        int separator = name?.LastIndexOf('\\') ?? -1;
        return separator >= 0 ? name.Substring(separator + 1) : name;
    }
}
