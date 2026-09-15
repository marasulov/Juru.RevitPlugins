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
    private const string BaseGlossaryFileName = "BaseGlossary.csv";

    /// <summary>
    ///     Путь к глоссарию. Пусто — используется файл в документах пользователя. Для общей работы здесь
    ///     указывают файл на сетевом диске или в репозитории проекта.
    /// </summary>
    public string? GlossaryPath { get; init; }

    /// <summary>
    ///     Словарь повторяющихся терминов, выверенный один раз на все проекты. Ищется по нормализованному
    ///     тексту, поэтому переносит термин между моделями с разной ручной расстановкой переносов.
    ///     Пусто — файл рядом с плагином.
    /// </summary>
    public string? BaseGlossaryPath { get; init; }

    /// <summary>Настройки машинного перевода.</summary>
    public TranslationSettings Translation { get; init; } = new();

    /// <summary>
    ///     Читает настройки: сначала файл рядом с плагином, затем пользовательский поверх него.
    /// </summary>
    /// <remarks>
    ///     Два файла, потому что писать рядом с плагином нельзя: он ставится в Program Files, и без
    ///     прав администратора запись оттуда падает. Файл рядом с плагином задаёт настройки проекта,
    ///     пользовательский — то, что человек поменял в окне настроек.
    /// </remarks>
    public static SchedulesConfig Load()
    {
        return TryRead(UserConfigPath) ?? TryRead(ResolveConfigPath()) ?? new SchedulesConfig();
    }

    /// <summary>Путь пользовательского файла настроек: его пишет окно настроек.</summary>
    public static string UserConfigPath
    {
        get
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "JuruPlugins",
                FileName);
        }
    }

    /// <summary>Сохраняет настройки в пользовательский файл. Возвращает причину, если не удалось.</summary>
    public string? TrySave()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(UserConfigPath)!);

            File.WriteAllText(
                UserConfigPath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));

            return null;
        }
        catch (IOException exception)
        {
            return exception.Message;
        }
        catch (UnauthorizedAccessException exception)
        {
            return exception.Message;
        }
    }

    private static SchedulesConfig? TryRead(string? path)
    {
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SchedulesConfig>(File.ReadAllText(path));
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public string ResolveGlossaryPath()
    {
        return string.IsNullOrWhiteSpace(GlossaryPath) ? Glossary.DefaultPath : GlossaryPath!;
    }

    public string ResolveBaseGlossaryPath()
    {
        if (!string.IsNullOrWhiteSpace(BaseGlossaryPath))
        {
            return BaseGlossaryPath!;
        }

        var directory = Path.GetDirectoryName(ResolveConfigPath() ?? string.Empty);

        return directory is null ? BaseGlossaryFileName : Path.Combine(directory, BaseGlossaryFileName);
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
