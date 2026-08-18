namespace Juru.RevitPlugins.Schedules.Services.Translation;

/// <summary>
///     Настройки службы перевода. Ключ хранится в переменной окружения, а не в файле: файл лежит рядом с
///     плагином и попадает в репозиторий.
/// </summary>
public sealed record TranslationSettings
{
    public string Provider { get; init; } = "Google";

    public string ApiKeyEnvironmentVariable { get; init; } = "JURU_TRANSLATE_KEY";

    public string SourceLanguage { get; init; } = "ru";

    public string TargetLanguage { get; init; } = "en";

    /// <summary>Сколько строк уходит в одном запросе.</summary>
    public int BatchSize { get; init; } = 50;
}
