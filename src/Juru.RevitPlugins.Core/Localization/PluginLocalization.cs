using System;
using System.Globalization;
using System.Windows;

namespace Juru.RevitPlugins.Core.Localization;

/// <summary>
/// Строки интерфейса плагина на одном языке, загруженные из XAML-словаря ресурсов.
/// </summary>
/// <remarks>
/// Не завязана на DI-контейнер: строится один раз в команде, открывающей окно (как
/// <see cref="Theming.JuruTheme" />), и передаётся дальше руками - в конструктор окна для XAML
/// (<see cref="ApplyTo" />) и в конструктор вьюмодели для сообщений (<see cref="GetString(string)" />).
/// <para>
/// Словари мёрджатся в <see cref="FrameworkElement.Resources" /> конкретного окна, а не в
/// <c>Application.Resources</c>: под Ревитом <c>Application.Current</c> равен <c>null</c>, и
/// словарь, смёрдженный туда, был бы недостижим для <c>{DynamicResource}</c> из разметки окна.
/// </para>
/// </remarks>
public sealed class PluginLocalization {
    private readonly ResourceDictionary _resources;

    private PluginLocalization(ResourceDictionary resources, CultureInfo culture) {
        _resources = resources;
        Culture = culture;
    }

    /// <summary>
    /// Язык, под который загружен словарь.
    /// </summary>
    public CultureInfo Culture { get; }

    /// <summary>
    /// Загружает словарь плагина под нужный язык.
    /// </summary>
    /// <param name="baseResourcePackUri">
    /// Путь вида "/Juru.RevitPlugins.Openings;component/assets/Localization/Language.xaml" -
    /// русский словарь, полный, служит запасным для отсутствующих ключей других языков.
    /// </param>
    /// <param name="culture">Язык, на котором должно показываться окно.</param>
    public static PluginLocalization Load(string baseResourcePackUri, CultureInfo culture) {
        var resources = new ResourceDictionary {
            Source = new Uri(baseResourcePackUri, UriKind.Relative)
        };

        if(!string.Equals(culture.Name, PluginCulture.Russian, StringComparison.OrdinalIgnoreCase)) {
            string cultureUri = baseResourcePackUri.Replace("Language.xaml", $"Language.{culture.Name}.xaml");
            resources.MergedDictionaries.Add(new ResourceDictionary {
                Source = new Uri(cultureUri, UriKind.Relative)
            });
        }

        return new PluginLocalization(resources, culture);
    }

    /// <summary>
    /// Загружает словарь плагина под нужный язык, взяв имя сборки из <paramref name="anchorType" />.
    /// </summary>
    /// <param name="anchorType">Любой тип из сборки плагина - обычно тип самой команды.</param>
    /// <param name="culture">Язык, на котором должно показываться окно.</param>
    /// <remarks>
    /// Словарь обязан лежать по пути "assets/Localization/Language.xaml" внутри сборки плагина -
    /// такому же, как у <see cref="Theming.JuruTheme" />, привязанному к теме, а не к языку.
    /// </remarks>
    public static PluginLocalization Load(System.Type anchorType, CultureInfo culture) {
        string assemblyName = anchorType.Assembly.GetName().Name;
        return Load($"/{assemblyName};component/assets/Localization/Language.xaml", culture);
    }

    /// <summary>
    /// Подключает словарь к окну, чтобы разметка могла брать строки через
    /// <c>{DynamicResource SomeKey}</c>.
    /// </summary>
    public void ApplyTo(FrameworkElement element) {
        element.Resources.MergedDictionaries.Add(_resources);
    }

    /// <summary>
    /// Строка по ключу - для сообщений, которые собираются в коде (заголовки/тексты
    /// <c>TaskDialog</c>, статусы), а не задаются в разметке.
    /// </summary>
    public string GetString(string key) {
        return _resources[key] as string ?? key;
    }

    /// <summary>
    /// Строка по ключу с подстановкой параметров через <see cref="string.Format(string, object[])" />.
    /// </summary>
    public string GetString(string key, params object[] args) {
        return string.Format(GetString(key), args);
    }
}
