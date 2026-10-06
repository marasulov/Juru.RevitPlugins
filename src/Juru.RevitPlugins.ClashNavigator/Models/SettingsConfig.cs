using System;
using System.Collections.Generic;
using System.IO;

using Juru.RevitPlugins.Core.ProjectConfigs;

using dosymep.Bim4Everyone;

using Newtonsoft.Json;

namespace Juru.RevitPlugins.ClashNavigator.Models;

internal class SettingsConfig : ProjectConfig {
    [JsonIgnore]
    public override string ProjectConfigPath { get; set; }

    [JsonIgnore]
    public override IConfigSerializer Serializer { get; set; }

    /// <summary>
    /// Флаг для включения 3D подрезки по коллизии
    /// </summary>
    public bool ApplySectionBoxSettings { get; set; } = true;

    /// <summary>
    /// Добавочный размер в мм для 3D подрезки суммарно вдоль оси
    /// </summary>
    public int SectionBoxOffset { get; set; } = 3000;

    /// <summary>
    /// Рисовать элементы выбранной коллизии цветом поверх модели на виде коллизий.
    /// </summary>
    public bool HighlightClashElements { get; set; } = true;

    /// <summary>
    /// При подсветке коллизии переводить остальную модель на виде коллизий в полутон.
    /// </summary>
    public bool HalftoneOtherElements { get; set; } = true;

    /// <summary>
    /// Адрес сервера коллизий для загрузки отчётов через REST API (см. <see cref="RevitClashReport.ServerClashes.ClashServerClient"/>).
    /// Пустое значение означает адрес из <see cref="ClashServerOptions.DefaultUrl"/>.
    /// </summary>
    public string ClashServerUrl { get; set; } = string.Empty;

    /// <summary>
    /// Загружать отчёт с сервера коллизий сразу при открытии навигатора.
    /// Включается автоматически после первой успешной загрузки с сервера.
    /// </summary>
    public bool AutoLoadFromServer { get; set; }

    /// <summary>
    /// Ширина и видимость колонок таблицы навигатора коллизий, ключ - заголовок колонки.
    /// Колонки, которых здесь нет, показываются с настройками из разметки окна.
    /// </summary>
    public Dictionary<string, ClashColumnSettings> NavigatorColumns { get; set; } = [];

    public static SettingsConfig GetSettingsConfig(IConfigSerializer serializer) {
        try {
            return Build(serializer);
        } catch(Exception) {
            // Файл настроек, сохранённый предыдущей версией плагина, может оказаться нечитаемым.
            string path = GetConfigFilePath();
            if(File.Exists(path)) {
                File.Delete(path);
            }
            return Build(serializer);
        }
    }

    private static SettingsConfig Build(IConfigSerializer serializer) {
        return new ProjectConfigBuilder()
            .SetSerializer(serializer)
            .SetPluginName(nameof(ClashNavigator))
            .SetRevitVersion(RevitVersionInfo.Version)
            .SetProjectConfigName(nameof(SettingsConfig) + ".json")
            .Build<SettingsConfig>();
    }

    private static string GetConfigFilePath() {
        return Path.Combine(
            RevitRepository.ProfilePath,
            RevitVersionInfo.Version,
            nameof(ClashNavigator),
            nameof(SettingsConfig) + ".json");
    }
}
