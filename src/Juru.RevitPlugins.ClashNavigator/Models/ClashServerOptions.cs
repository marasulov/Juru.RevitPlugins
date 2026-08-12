using System;
using System.IO;
using System.Reflection;

using Newtonsoft.Json;

namespace Juru.RevitPlugins.ClashNavigator.Models;

/// <summary>
/// Адрес сервера коллизий, заданный файлом <c>ClashServer.json</c> рядом со сборкой плагина.
/// Файл правится на месте, без пересборки.
/// </summary>
internal static class ClashServerOptions {
    private const string _fileName = "ClashServer.json";

    /// <summary>
    /// Адрес из файла рядом со сборкой. Пустая строка, если файла нет или он нечитаем.
    /// </summary>
    public static string DefaultUrl { get; } = ReadDefaultUrl();

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

            var options = JsonConvert.DeserializeObject<ClashServerFile>(File.ReadAllText(path));
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
