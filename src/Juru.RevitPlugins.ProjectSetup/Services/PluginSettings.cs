using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Juru.RevitPlugins.ProjectSetup.Services;

/// <summary>
/// Настройки пользователя: пароль страницы подготовки ClashTracker. Хранится в профиле
/// пользователя, зашифрованным для текущей учётной записи Windows (DPAPI): открыть его может
/// только тот, кто его ввёл, и только на этой машине.
/// </summary>
internal sealed class PluginSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Juru", "ProjectSetup", "settings.json");

    public string ProtectedPassword { get; set; } = "";

    public string Password
    {
        get
        {
            if (string.IsNullOrEmpty(ProtectedPassword)) return "";
            try
            {
                byte[] bytes = ProtectedData.Unprotect(Convert.FromBase64String(ProtectedPassword), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException)
            {
                return "";
            }
        }
        set => ProtectedPassword = string.IsNullOrEmpty(value)
            ? ""
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
    }

    public static PluginSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<PluginSettings>(File.ReadAllText(FilePath)) ?? new PluginSettings()
                : new PluginSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new PluginSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
    }
}
