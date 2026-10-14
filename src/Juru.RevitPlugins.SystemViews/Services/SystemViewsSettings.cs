using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

using Juru.RevitPlugins.SystemViews.Models;

namespace Juru.RevitPlugins.SystemViews.Services;

/// <summary>
///     Выбор пользователя с прошлого запуска. Хранится в профиле пользователя, а не рядом с плагином:
///     плагин ставится в Program Files, писать туда без прав администратора нельзя.
/// </summary>
public sealed record SystemViewsSettings
{
    private const string FileName = "SystemViews.json";

    /// <summary>Имя параметра системы. Пусто - «Имя системы».</summary>
    public string? ParameterName { get; init; }

    /// <summary>Выбранные категории (имена <c>BuiltInCategory</c>). Пусто - категории по умолчанию.</summary>
    public List<string>? Categories { get; init; }

    public string ViewNamePrefix { get; init; } = "Схема ";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TemplateMode TemplateMode { get; init; } = TemplateMode.AsSourceView;

    /// <summary>Имя шаблона для <see cref="TemplateMode.Specific" />: id в другом файле будет другим.</summary>
    public string? TemplateName { get; init; }

    public bool FitSectionBox { get; init; }

    /// <summary>Системы, отмеченные в прошлый раз: в следующий раз они отмечены сразу.</summary>
    public List<string>? Systems { get; init; }

    public static string FilePath
    {
        get
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "JuruPlugins",
                FileName);
        }
    }

    /// <summary>Читает настройки. Нет файла или он испорчен - настройки по умолчанию.</summary>
    public static SystemViewsSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<SystemViewsSettings>(File.ReadAllText(FilePath)) ?? new SystemViewsSettings()
                : new SystemViewsSettings();
        }
        catch (JsonException)
        {
            return new SystemViewsSettings();
        }
        catch (IOException)
        {
            return new SystemViewsSettings();
        }
    }

    /// <summary>Сохраняет настройки. Возвращает причину, если не удалось.</summary>
    public string? TrySave()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));

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
}
