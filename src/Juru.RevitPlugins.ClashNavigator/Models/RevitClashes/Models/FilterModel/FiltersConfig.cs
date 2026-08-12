using System;
using System.Collections.Generic;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Core.ProjectConfigs;

using dosymep.Bim4Everyone;

using Newtonsoft.Json;

namespace Juru.RevitPlugins.ClashNavigator.Models.FilterModel;
internal class FiltersConfig : ProjectConfig {
    public string RevitVersion { get; set; }
    public List<Filter> Filters { get; set; } = [];
    [JsonIgnore]
    public override string ProjectConfigPath { get; set; }
    [JsonIgnore]
    public override IConfigSerializer Serializer { get; set; }
    public static FiltersConfig GetFiltersConfig(string revitFileName, Document document) {
        return document is null
            ? throw new ArgumentNullException(nameof(document))
            : new ProjectConfigBuilder()
            .SetSerializer(new RevitClashConfigSerializer(new RevitClashesSerializationBinder(), document))
            .SetPluginName(nameof(Juru.RevitPlugins.ClashNavigator))
            .SetRelativePath(revitFileName)
            .SetRevitVersion(RevitVersionInfo.Version)
            .SetProjectConfigName(nameof(FiltersConfig) + ".json")
            .Build<FiltersConfig>();
    }
}
