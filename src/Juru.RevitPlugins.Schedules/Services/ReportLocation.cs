using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Juru.RevitPlugins.Schedules.Services;

/// <summary>
///     Отводит папку под отчёт плагина в документах пользователя.
/// </summary>
public static class ReportLocation
{
    public static string Create(string reportKind, string modelName, DateTime timestamp)
    {
        var reportDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "JuruPlugins",
            reportKind,
            $"{SanitizeFolderName(modelName)}_{timestamp:yyyyMMdd_HHmmss}");

        Directory.CreateDirectory(reportDirectory);

        return reportDirectory;
    }

    private static string SanitizeFolderName(string modelName)
    {
        var sanitized = new StringBuilder(modelName.Length);

        foreach (var symbol in modelName)
        {
            sanitized.Append(Path.GetInvalidFileNameChars().Contains(symbol) ? '_' : symbol);
        }

        return sanitized.ToString();
    }
}
