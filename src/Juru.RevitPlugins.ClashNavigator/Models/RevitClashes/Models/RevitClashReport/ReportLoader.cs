using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

using Juru.RevitPlugins.ClashNavigator.Models.Clashes;
using Juru.RevitPlugins.ClashNavigator.Models.Interfaces;

namespace Juru.RevitPlugins.ClashNavigator.Models.RevitClashReport;
internal class ReportLoader {
    public static IEnumerable<ReportModel> GetReports(RevitRepository revitRepository, string path) {
        revitRepository.InitializeDocInfos();

        var loaders = new List<IClashesLoader>() {
            new PluginClashesLoader(path, revitRepository.Doc),
            new NavisXmlClashesLoader(revitRepository, path)
        };

        var loader = loaders.FirstOrDefault(item => item.IsValid());
        if(loader == null) {
            throw new ArgumentException($"Неверный формат файла {path}");
        }

        var reports = loader.GetReports();
        foreach(var report in reports) {
            // Keep every imported row visible. The previous filter silently
            // removed the entire report when a linked RVT could not be
            // identified from XML, making the navigator appear empty.
            report.Clashes = new ReadOnlyCollection<ClashModel>(report.Clashes
                .Select(item => item.SetRevitRepository(revitRepository))
                .ToArray());
        }
        return reports;
    }
}
