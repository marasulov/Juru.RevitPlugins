using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace ClashNavigator.Server;

/// <summary>
/// Учётная запись Windows, под которой запущен Ревит. Автора изменения статуса коллизии
/// определяем по ней, а не по имени пользователя Autodesk: в базу нужно писать сотрудника организации.
/// </summary>
internal static class WindowsUser {
    // EXTENDED_NAME_FORMAT.NameDisplay - отображаемое имя пользователя ("Yusuf Marasulov").
    private const int _nameDisplay = 3;
    private const int _errorMoreData = 234;

    /// <summary>
    /// Полное имя учётной записи в формате DOMAIN\user. Им коллизия подписывается в базе.
    /// </summary>
    public static string Name { get; } = GetName();

    /// <summary>
    /// Имя учётной записи без домена.
    /// </summary>
    public static string Account { get; } = GetAccount(Name);

    /// <summary>
    /// Имя пользователя, как оно записано в домене или учётной записи Windows ("Yusuf Marasulov").
    /// Совпадает с <see cref="Account"/>, если получить его не удалось.
    /// </summary>
    public static string FullName { get; } = GetFullName() ?? Account;

    /// <summary>
    /// Автор изменения для показа в таблице: имя пользователя и его учётная запись.
    /// </summary>
    public static string DisplayName { get; } = FullName == Account
        ? Account
        : $"{FullName} ({Account})";

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

    /// <summary>
    /// Спрашивает отображаемое имя у Windows. На машине в домене или с учётной записью Microsoft
    /// возвращает имя и фамилию сотрудника, иначе null.
    /// </summary>
    private static string GetFullName() {
        try {
            var buffer = new StringBuilder(256);
            uint size = (uint) buffer.Capacity;
            if(!GetUserNameEx(_nameDisplay, buffer, ref size)) {
                if(Marshal.GetLastWin32Error() != _errorMoreData) {
                    return null;
                }

                // Windows сообщила нужный размер буфера - спрашиваем ещё раз с ним.
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
