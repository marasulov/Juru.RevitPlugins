using System;
using System.IO;
using System.Text.RegularExpressions;

using dosymep.Bim4Everyone;
using dosymep.Bim4Everyone.ProjectConfigs;

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
    /// Адрес сервера коллизий для загрузки отчётов через REST API (см. <see cref="RevitClashReport.ServerClashes.ClashServerClient"/>)
    /// </summary>
    public string ClashServerUrl { get; set; } = string.Empty;


    // Сериализатор конфигураций из dosymep.Bim4Everyone использует внутри себя pyRevitLabs.Json,
    // а не Newtonsoft.Json. Это разные типы атрибутов JsonIgnore, поэтому [JsonIgnore] выше
    // (из Newtonsoft.Json) им не учитывается, и свойство Serializer (интерфейс IConfigSerializer)
    // иногда всё равно попадает в сохранённый файл. При следующей загрузке десериализация такого
    // файла ломается с JsonSerializationException, так как интерфейс нельзя создать напрямую.
    private static readonly Regex _serializerNodePattern = new(
        @",?\s*""Serializer""\s*:\s*\{[^{}]*\}", RegexOptions.Compiled);

    public static SettingsConfig GetSettingsConfig(IConfigSerializer serializer) {
        try {
            return Build(serializer);
        } catch(Exception) {
            string path = GetConfigFilePath();
            if(TryRemoveSerializerNode(path)) {
                try {
                    return Build(serializer);
                } catch(Exception) {
                    // восстановление не помогло, сбрасываем файл настроек ниже
                }
            }

            if(File.Exists(path)) {
                File.Delete(path);
            }
            return Build(serializer);
        }
    }

    private static SettingsConfig Build(IConfigSerializer serializer) {
        return new dosymep.Bim4Everyone.ProjectConfigs.ProjectConfigBuilder()
            .SetSerializer(serializer)
            .SetPluginName(nameof(ClashNavigator))
            .SetRevitVersion(ModuleEnvironment.RevitVersion)
            .SetProjectConfigName(nameof(SettingsConfig) + ".json")
            .Build<SettingsConfig>();
    }

    private static string GetConfigFilePath() {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "dosymep",
            ModuleEnvironment.RevitVersion,
            nameof(ClashNavigator),
            nameof(SettingsConfig) + ".json");
    }

    private static bool TryRemoveSerializerNode(string path) {
        if(!File.Exists(path)) {
            return false;
        }

        try {
            string json = File.ReadAllText(path);
            string repaired = _serializerNodePattern.Replace(json, string.Empty);
            if(repaired == json) {
                return false;
            }

            File.WriteAllText(path, repaired);
            return true;
        } catch(Exception) {
            return false;
        }
    }
}
