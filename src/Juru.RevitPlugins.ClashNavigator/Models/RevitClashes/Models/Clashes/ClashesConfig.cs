using System.Collections.Generic;

using Juru.RevitPlugins.Core.ProjectConfigs;

using dosymep.Bim4Everyone;
using dosymep.Serializers;

using Newtonsoft.Json;

namespace Juru.RevitPlugins.ClashNavigator.Models.Clashes;
internal class ClashesConfig : ProjectConfig {
    [JsonIgnore]
    public override string ProjectConfigPath { get; set; }
    [JsonIgnore]
    public override IConfigSerializer Serializer { get; set; }
    public List<ClashModel> Clashes { get; set; } = [];
    public static ClashesConfig GetClashesConfig(string revitObjectName, string configName) {
        return new ProjectConfigBuilder()
            .SetSerializer(new ConfigSerializer())
            .SetPluginName(nameof(Juru.RevitPlugins.ClashNavigator))
            .SetProfilePath(RevitRepository.LocalProfilePath)
            .SetRelativePath(revitObjectName)
            .SetRevitVersion(RevitVersionInfo.Version)
            .SetProjectConfigName(configName + ".json")
            .Build<ClashesConfig>();
    }
}
