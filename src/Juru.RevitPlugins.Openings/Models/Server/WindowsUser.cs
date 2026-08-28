using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace Juru.RevitPlugins.Openings.Models.Server;

/// <summary>
/// Учётная запись Windows, под которой запущен Ревит. Автора выдачи задания и решения по
/// согласованию определяем по ней, а не по имени пользователя Autodesk: серверу нужен сотрудник
/// организации.
/// </summary>
/// <remarks>
/// Дублирует <c>Juru.RevitPlugins.ClashNavigator.Models.WindowsUser</c> - оба плагина решают одну и
/// ту же задачу независимо; общего проекта для таких мелких помощников пока нет.
/// </remarks>
internal static class WindowsUser {
    private const int _nameDisplay = 3;
    private const int _errorMoreData = 234;

    /// <summary>
    /// Полное имя учётной записи в формате DOMAIN\user. Им подписывается выдача и решение.
    /// </summary>
    public static string Name { get; } = GetName();

    /// <summary>
    /// Имя учётной записи без домена.
    /// </summary>
    public static string Account { get; } = GetAccount(Name);

    /// <summary>
    /// Имя пользователя, как оно записано в домене ("Yusuf Marasulov").
    /// Совпадает с <see cref="Account"/>, если получить его не удалось.
    /// </summary>
    public static string FullName { get; } = GetFullName() ?? Account;

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

    private static string GetAccount(string name) {
        int separator = name?.LastIndexOf('\\') ?? -1;
        return separator >= 0 ? name.Substring(separator + 1) : name;
    }

    private static string GetFullName() {
        try {
            var buffer = new StringBuilder(256);
            uint size = (uint) buffer.Capacity;
            if(!GetUserNameEx(_nameDisplay, buffer, ref size)) {
                if(Marshal.GetLastWin32Error() != _errorMoreData) {
                    return null;
                }

                buffer = new StringBuilder((int) size);
                if(!GetUserNameEx(_nameDisplay, buffer, ref size)) {
                    return null;
                }
            }

            string fullName = buffer.ToString().Trim();
            return string.IsNullOrEmpty(fullName) ? null : fullName;
        } catch(DllNotFoundException) {
            return null;
        } catch(EntryPointNotFoundException) {
            return null;
        }
    }

    [DllImport("secur32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetUserNameEx(int nameFormat, StringBuilder nameBuffer, ref uint size);
}
