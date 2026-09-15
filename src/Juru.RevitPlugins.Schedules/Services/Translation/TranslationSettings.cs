namespace Juru.RevitPlugins.Schedules.Services.Translation;

/// <summary>
///     Настройки службы перевода.
/// </summary>
/// <remarks>
///     Ключ хранится в переменной окружения, а не в файле настроек: файл лежит рядом с плагином и
///     попадает в репозиторий. Имя переменной у каждой службы своё, чтобы ключи DeepL и Google
///     не вытесняли друг друга при переключении службы.
/// </remarks>
public sealed record TranslationSettings
{
    public const string DeepL = "DeepL";
    public const string Google = "Google";

    public string Provider { get; init; } = Google;

    /// <summary>
    ///     Общее имя переменной окружения с ключом. Оставлено для настроек, написанных до появления
    ///     отдельных переменных: если у службы своей переменной нет, ключ берётся отсюда.
    /// </summary>
    public string ApiKeyEnvironmentVariable { get; init; } = "JURU_TRANSLATE_KEY";

    public string DeepLKeyEnvironmentVariable { get; init; } = "JURU_DEEPL_KEY";

    public string GoogleKeyEnvironmentVariable { get; init; } = "JURU_GOOGLE_KEY";

    public string SourceLanguage { get; init; } = "ru";

    public string TargetLanguage { get; init; } = "en";

    /// <summary>Сколько строк уходит в одном запросе.</summary>
    public int BatchSize { get; init; } = 50;

    /// <summary>Оставлять исходник перед переводом: «Кабинет / Office» вместо просто «Office».</summary>
    public bool KeepSourceText { get; init; }

    /// <summary>Разделитель исходника и перевода в двуязычном значении.</summary>
    public string Separator { get; init; } = "/";

    /// <summary>Имя переменной окружения с ключом выбранной службы.</summary>
    public string ResolveKeyVariable()
    {
        return ResolveKeyVariable(Provider);
    }

    /// <summary>Имя переменной окружения с ключом указанной службы.</summary>
    public string ResolveKeyVariable(string provider)
    {
        var own = provider.Trim().ToLowerInvariant() switch
        {
            "deepl" => DeepLKeyEnvironmentVariable,
            "google" => GoogleKeyEnvironmentVariable,
            _ => string.Empty
        };

        return string.IsNullOrWhiteSpace(own) ? ApiKeyEnvironmentVariable : own;
    }
}
