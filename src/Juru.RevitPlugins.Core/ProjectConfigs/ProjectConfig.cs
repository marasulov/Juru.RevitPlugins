using System;
using System.IO;

using JetBrains.Annotations;

using Newtonsoft.Json;

namespace Juru.RevitPlugins.Core.ProjectConfigs;

/// <summary>
/// Конфигурация плагина, хранимая в файле.
/// </summary>
[PublicAPI]
public abstract class ProjectConfig {
    /// <summary>
    /// Полный путь к файлу конфигурации.
    /// </summary>
    [JsonIgnore]
    public abstract string ProjectConfigPath { get; set; }

    /// <summary>
    /// Сериализатор, которым конфигурация читается и записывается.
    /// </summary>
    [JsonIgnore]
    public abstract IConfigSerializer Serializer { get; set; }

    /// <summary>
    /// Язык интерфейса плагина, выбранный пользователем в настройках. Пустая строка - следовать
    /// за языком Ревита, см. <see cref="Juru.RevitPlugins.Core.Localization.PluginCulture" />.
    /// </summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>
    /// Записывает конфигурацию в <see cref="ProjectConfigPath" />.
    /// Недостающие каталоги создаются.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Serializer" /> или <see cref="ProjectConfigPath" /> не назначен.
    /// </exception>
    public void SaveProjectConfig() {
        if(Serializer is null) {
            throw new InvalidOperationException($"Для сохранения конфигурации требуется назначить {nameof(Serializer)}.");
        }

        if(string.IsNullOrEmpty(ProjectConfigPath)) {
            throw new InvalidOperationException($"Для сохранения конфигурации требуется назначить {nameof(ProjectConfigPath)}.");
        }

        string directoryPath = Path.GetDirectoryName(ProjectConfigPath);
        if(!string.IsNullOrEmpty(directoryPath)) {
            Directory.CreateDirectory(directoryPath);
        }

        File.WriteAllText(ProjectConfigPath, Serializer.Serialize(this));
    }
}
