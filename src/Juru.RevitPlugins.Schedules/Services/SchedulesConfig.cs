using System;
using System.IO;
using System.Reflection;
using System.Text.Json;

using Juru.RevitPlugins.Schedules.Services.Translation;

namespace Juru.RevitPlugins.Schedules.Services;

/// <summary>
///     Настройки плагина. Файл лежит рядом с установленным плагином и правится без пересборки.
/// </summary>
public sealed record SchedulesConfig
{
    private const string FileName = "Schedules.json";

    /// <summary>
    ///     Путь к глоссарию. Пусто — используется файл в документах пользователя. Для общей работы здесь
    ///     указывают файл на сетевом диске или в репозитории проекта.
    /// </summary>
    public string? GlossaryPath { get; init; }

    /// <summary>Настройки машинного перевода.</summary>
    public TranslationSettings Translation { get; init; } = new();

    public static SchedulesConfig Load()
    {
        var path = ResolveConfigPath();

        if (path is null || !File.Exists(path))
        {
            return new SchedulesConfig();
        }

        try
        {
            return JsonSerializer.Deserialize<SchedulesConfig>(File.ReadAllText(path)) ?? new SchedulesConfig();
        }
        catch (JsonException)
        {
            return new SchedulesConfig();
        }
        catch (IOException)
        {
            return new SchedulesConfig();
        }
    }

    public string ResolveGlossaryPath()
    {
        return string.IsNullOrWhiteSpace(GlossaryPath) ? Glossary.DefaultPath : GlossaryPath!;
    }

    private static string? ResolveConfigPath()
    {
        var assemblyPath = Assembly.GetExecutingAssembly().Location;

        if (string.IsNullOrEmpty(assemblyPath))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(assemblyPath);

        return directory is null ? null : Path.Combine(directory, FileName);
    }
}
