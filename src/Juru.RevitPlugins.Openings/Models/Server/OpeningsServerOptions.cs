using System;
using System.IO;
using System.Reflection;
using System.Text.Json;

using Juru.RevitPlugins.Openings.Models.Configs;

namespace Juru.RevitPlugins.Openings.Models.Server;

/// <summary>
/// Адрес сервера заданий на отверстия, заданный файлом <c>ClashServer.json</c> рядом со сборкой
/// плагина - тот же файл и тот же адрес, с которого ClashNavigator читает <c>api/projects</c>.
/// Файл правится на месте, без пересборки.
/// </summary>
internal static class OpeningsServerOptions {
    private const string _fileName = "ClashServer.json";

    // Newtonsoft (ClashNavigator) deserializes property names case-insensitively by default;
    // System.Text.Json does not, so the shared file's "Url"/"url" casing must be matched explicitly.
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Адрес из файла рядом со сборкой. Пустая строка, если файла нет или он нечитаем.
    /// </summary>
    public static string DefaultUrl { get; } = ReadDefaultUrl();

    /// <summary>
    /// Адрес сервера: настройка пользователя (<see cref="OpeningsConfig.ServerUrl"/>) переопределяет
    /// <see cref="DefaultUrl"/>. Пустая строка, если адрес не задан ни там, ни там.
    /// </summary>
    public static string ResolveUrl(OpeningsConfig config) {
        return string.IsNullOrWhiteSpace(config?.ServerUrl)
            ? DefaultUrl
            : config.ServerUrl;
    }

    private static string ReadDefaultUrl() {
        try {
            string directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if(string.IsNullOrEmpty(directory)) {
                return string.Empty;
            }

            string path = Path.Combine(directory, _fileName);
            if(!File.Exists(path)) {
                return string.Empty;
            }

            var options = JsonSerializer.Deserialize<ClashServerFile>(File.ReadAllText(path), _jsonOptions);
            return options?.Url?.Trim() ?? string.Empty;
        } catch(IOException) {
            return string.Empty;
        } catch(UnauthorizedAccessException) {
            return string.Empty;
        } catch(JsonException) {
            return string.Empty;
        }
    }

    private sealed class ClashServerFile {
        public string Url { get; set; }
    }
}
