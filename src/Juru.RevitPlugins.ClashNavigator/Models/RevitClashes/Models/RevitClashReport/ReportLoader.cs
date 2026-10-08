using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

using dosymep.SimpleServices;

using Juru.RevitPlugins.ClashNavigator.Models.Clashes;
using Juru.RevitPlugins.ClashNavigator.Models.Interfaces;

namespace Juru.RevitPlugins.ClashNavigator.Models.RevitClashReport;
internal class ReportLoader {
    /// <param name="localization">
    /// Служба локализации для описаний объектов отчёта, не найденных в Revit.
    /// </param>
    public static IEnumerable<ReportModel> GetReports(
        RevitRepository revitRepository, string path, ILocalizationService localization = null) {
        revitRepository.InitializeDocInfos();

        var loaders = new List<IClashesLoader>() {
            new PluginClashesLoader(path, revitRepository.Doc),
            new NavisXmlClashesLoader(revitRepository, path, localization)
        };

        var loader = loaders.FirstOrDefault(item => item.IsValid());
        if(loader == null) {
            throw new ArgumentException($"Неверный формат файла {path}");
        }

        // Материализуем сразу: загрузчик отдаёт ленивую последовательность, и без этого
        // возвращённый результат разбирался бы из XML заново, теряя изменения цикла ниже.
        var reports = loader.GetReports().ToList();
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
