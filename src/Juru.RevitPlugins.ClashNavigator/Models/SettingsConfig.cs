
using System.Windows.Media;

using dosymep.Bim4Everyone;
using dosymep.Bim4Everyone.ProjectConfigs;

using Newtonsoft.Json;

namespace Juru.RevitPlugins.ClashNavigator.Models;

internal class SettingsConfig : ProjectConfig {
    [JsonIgnore]
    public override string ProjectConfigPath { get; set; }

    [JsonIgnore]
    public override IConfigSerializer Serializer { get; set; }

    public ElementVisibilitySettings MainElementVisibilitySettings { get; set; } = new ElementVisibilitySettings() {
        Color = System.Windows.Media.Color.FromRgb(255, 255, 0),
        Transparency = 40
    };

    public ElementVisibilitySettings SecondElementVisibilitySettings { get; set; } = new ElementVisibilitySettings() {
        Color = System.Windows.Media.Color.FromRgb(0, 255, 255),
        Transparency = 40
    };

    public SectionBoxMode SectionBoxModeSettings { get; set; } = SectionBoxMode.AroundCollision;

    /// <summary>
    /// Флаг для включения 3D подрезки по коллизии
    /// </summary>
    public bool ApplySectionBoxSettings { get; set; } = true;

    /// <summary>
    /// Флаг для включения изоляции элементов коллизии
    /// </summary>
    public bool ApplyIsolationSettings { get; set; } = false;

    /// <summary>
    /// Флаг для включения раскраски элементов коллизии
    /// </summary>
    public bool ApplyColorSettings { get; set; } = false;

    /// <summary>
    /// Добавочный размер в мм для 3D подрезки суммарно вдоль оси
    /// </summary>
    public int SectionBoxOffset { get; set; } = 3000;

    /// <summary>
    /// Названия дополнительных параметров обоих элементов коллизий для отображения в навигаторе
    /// </summary>
    public string[] ParamNames { get; set; } = [];


    public static SettingsConfig GetSettingsConfig(IConfigSerializer serializer) {
        return new dosymep.Bim4Everyone.ProjectConfigs.ProjectConfigBuilder()
            .SetSerializer(serializer)
            .SetPluginName(nameof(ClashNavigator))
            .SetRevitVersion(ModuleEnvironment.RevitVersion)
            .SetProjectConfigName(nameof(SettingsConfig) + ".json")
            .Build<SettingsConfig>();
    }
}
