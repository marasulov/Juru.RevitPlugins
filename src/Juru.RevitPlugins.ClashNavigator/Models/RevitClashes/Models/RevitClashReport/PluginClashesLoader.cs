using System;
using System.Collections.Generic;
using System.IO;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.ClashNavigator.Models.Clashes;
using Juru.RevitPlugins.ClashNavigator.Models.Interfaces;

namespace Juru.RevitPlugins.ClashNavigator.Models.RevitClashReport;
internal class PluginClashesLoader : IClashesLoader {
    private readonly Document _document;

    public PluginClashesLoader(string path, Document document) {
        if(document is null) { throw new ArgumentNullException(nameof(document)); }

        FilePath = path;
        _document = document;
    }

    public string FilePath { get; }

    public IEnumerable<ReportModel> GetReports() {
        try {
            var configLoader = new ConfigLoader(_document);
            var clashes = configLoader.Load<ClashesConfig>(FilePath).Clashes;
            string name = Path.GetFileNameWithoutExtension(FilePath);
            return new ReportModel[] { new(name, clashes) };
        } catch(Exception) {
            throw new ArgumentException("Неверный файл конфигурации.");
        }

    }

    public bool IsValid() {
        return FilePath.EndsWith(".json");
    }
}
