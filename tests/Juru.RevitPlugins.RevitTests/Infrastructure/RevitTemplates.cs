using System.IO;

using Autodesk.Revit.ApplicationServices;

using TUnit.Core;

namespace Juru.RevitPlugins.RevitTests.Infrastructure;

/// <summary>
/// Шаблоны, которые ставятся вместе с Ревитом. Берутся русские - на них работают у нас.
/// </summary>
public static class RevitTemplates {
    /// <summary>
    /// Архитектурный шаблон: уровни, типы стен и перекрытий.
    /// </summary>
    public static string Architecture(Application application) {
        return Resolve(application, "DefaultRUSRUS.rte");
    }

    /// <summary>
    /// Шаблон инженерных систем: типы воздуховодов, труб и систем.
    /// </summary>
    public static string Systems(Application application) {
        return Resolve(application, "Systems-DefaultRUSRUS.rte");
    }

    private static string Resolve(Application application, string fileName) {
        string path = Path.Combine(
            $@"C:\ProgramData\Autodesk\RVT {application.VersionNumber}\Templates\Russian", fileName);
        if(!File.Exists(path)) {
            Skip.Test($"Нет шаблона Ревита {path}");
        }

        return path;
    }
}
