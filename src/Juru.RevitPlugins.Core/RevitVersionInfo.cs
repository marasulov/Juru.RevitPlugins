using Autodesk.Revit.DB;

using JetBrains.Annotations;

namespace Juru.RevitPlugins.Core;

/// <summary>
/// Версия Revit, в котором выполняется плагин.
/// </summary>
[PublicAPI]
public static class RevitVersionInfo {
    /// <summary>
    /// Год версии Revit, например <c>2024</c>.
    /// </summary>
    public static string Version { get; } = GetVersion();

    private static string GetVersion() {
        // Сборка RevitAPI.dll версионируется как 24.0.0.0 для Revit 2024.
        int majorVersion = typeof(Document).Assembly.GetName().Version.Major;
        return $"20{majorVersion}";
    }
}
