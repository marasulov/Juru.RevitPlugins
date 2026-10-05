using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Resources;

namespace Juru.RevitPlugins.Core.Localization;

/// <summary>
/// Путь к XAML-ресурсу плагина, верный и для отдельной сборки, и для слитой ILRepack-ом.
/// </summary>
/// <remarks>
/// В релизе все плагины слиты в <c>Juru.RevitPlugins.App.dll</c>, и ILRepack кладёт BAML
/// каждой исходной сборки под префиксом её имени: <c>juru.revitplugins.clashnavigator/assets/...</c>.
/// Пути внутри XAML он переписывает сам, а строку, собранную в C# из имени сборки, - нет:
/// <c>/Juru.RevitPlugins.App;component/assets/Localization/Language.xaml</c> в слитой сборке
/// не находится, и окна остаются без словаря, то есть без единой надписи. В отладке, где
/// сборки не слиты, тот же путь без префикса верен - поэтому выбирается по факту, а не по
/// конфигурации.
/// </remarks>
public static class PluginResourceUri {
    private static readonly ConcurrentDictionary<Assembly, HashSet<string>> _keys = new();

    /// <summary>
    /// Возвращает относительный pack-путь к ресурсу сборки, в которой лежит <paramref name="anchorType" />.
    /// </summary>
    /// <param name="anchorType">
    /// Любой тип плагина. Корень его пространства имён совпадает с именем исходной сборки,
    /// по нему и выбирается префикс в слитой сборке.
    /// </param>
    /// <param name="relativePath">Путь внутри проекта, например "assets/Localization/Language.xaml".</param>
    public static string Get(Type anchorType, string relativePath) {
        if(anchorType is null) {
            throw new ArgumentNullException(nameof(anchorType));
        }

        var assembly = anchorType.Assembly;
        string assemblyName = assembly.GetName().Name;
        var keys = _keys.GetOrAdd(assembly, ReadKeys);

        string resourceKey = ToResourceKey(relativePath);
        if(keys.Contains(resourceKey)) {
            return $"/{assemblyName};component/{relativePath}";
        }

        string suffix = "/" + resourceKey;
        string prefix = keys
            .Where(key => key.EndsWith(suffix, StringComparison.Ordinal))
            .Select(key => key.Substring(0, key.Length - suffix.Length))
            .Where(item => anchorType.Namespace != null
                           && anchorType.Namespace.StartsWith(item, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.Length)
            .FirstOrDefault();

        // Ресурса нет ни так, ни так - возвращается обычный путь, чтобы ошибка загрузки
        // назвала именно его, а не выдуманный.
        return prefix is null
            ? $"/{assemblyName};component/{relativePath}"
            : $"/{assemblyName};component/{prefix}/{relativePath}";
    }

    private static string ToResourceKey(string relativePath) {
        string key = relativePath.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
        return key.EndsWith(".xaml", StringComparison.Ordinal)
            ? key.Substring(0, key.Length - ".xaml".Length) + ".baml"
            : key;
    }

    private static HashSet<string> ReadKeys(Assembly assembly) {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        string resourceName = assembly.GetName().Name + ".g.resources";
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if(stream is null) {
            return keys;
        }

        using var reader = new ResourceReader(stream);
        var enumerator = reader.GetEnumerator();
        while(enumerator.MoveNext()) {
            keys.Add((string) enumerator.Key);
        }
        return keys;
    }
}
