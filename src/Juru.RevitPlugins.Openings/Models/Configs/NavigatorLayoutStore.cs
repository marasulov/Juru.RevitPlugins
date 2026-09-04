using System;
using System.IO;
using System.Text.Json;

using Juru.RevitPlugins.Core;
using Juru.RevitPlugins.Core.Profiles;

namespace Juru.RevitPlugins.Openings.Models.Configs;

/// <summary>
/// Читает и пишет настройки окон навигаторов - то, что каждый пользователь настроил под себя.
/// </summary>
/// <remarks>
/// Файлы лежат в локальном профиле пользователя, а не в <see cref="OpeningsConfig"/>: тот раскатывает
/// BIM-менеджер на всех, а нужные колонки - дело того, кто смотрит окно. Общего варианта у этих
/// настроек нет, поэтому и чтения из сетевого профиля здесь нет. У каждого окна свой файл: колонки
/// у окон разные, и один общий файл сделал бы скрытые колонки одного окна скрытыми в другом.
/// </remarks>
public static class NavigatorLayoutStore {
    private const string _pluginFolderName = "Openings";

    private static readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    /// <summary>
    /// Читает настройки окна. Если файла нет или он испорчен - настройки по умолчанию: окно
    /// не должно открываться с ошибкой из-за своих же украшений.
    /// </summary>
    public static T Load<T>(string fileName) where T : new() {
        try {
            string path = GetPath(fileName);
            return File.Exists(path)
                ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), _options) ?? new T()
                : new T();
        } catch(Exception exception) when(exception is IOException
            or UnauthorizedAccessException
            or JsonException) {
            return new T();
        }
    }

    /// <summary>
    /// Сохраняет настройки окна. Ошибку записи глотает: сорванное сохранение украшений не должно
    /// прерывать работу с заданиями.
    /// </summary>
    public static void Save<T>(string fileName, T layout) {
        try {
            string path = GetPath(fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonSerializer.Serialize(layout, _options));
        } catch(Exception exception) when(exception is IOException or UnauthorizedAccessException) {
            // Настройки останутся прежними при следующем запуске - это всё, чем это грозит.
        }
    }

    private static string GetPath(string fileName) {
        return Path.Combine(ProfilePaths.Local, RevitVersionInfo.Version, _pluginFolderName, fileName);
    }
}
