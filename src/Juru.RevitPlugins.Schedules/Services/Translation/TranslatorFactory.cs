using System;
using System.Net.Http;

namespace Juru.RevitPlugins.Schedules.Services.Translation;

/// <summary>
///     Собирает службу перевода по настройкам. Один <c>HttpClient</c> на процесс: новый на каждый запрос
///     исчерпывает сокеты.
/// </summary>
public static class TranslatorFactory
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromMinutes(2) };

    /// <summary>
    ///     Возвращает службу перевода либо описание причины, по которой её не удалось собрать.
    /// </summary>
    public static ITextTranslator? TryCreate(TranslationSettings settings, out string? reason)
    {
        var apiKey = Environment.GetEnvironmentVariable(settings.ApiKeyEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            reason = $"Не задан ключ: переменная окружения {settings.ApiKeyEnvironmentVariable} пуста.";
            return null;
        }

        reason = null;

        return settings.Provider.Trim().ToLowerInvariant() switch
        {
            "google" => new GoogleTranslator(HttpClient, apiKey!, settings),
            "deepl" => new DeepLTranslator(HttpClient, apiKey!, settings),
            _ => FailUnknownProvider(settings.Provider, out reason)
        };
    }

    private static ITextTranslator? FailUnknownProvider(string provider, out string? reason)
    {
        reason = $"Неизвестная служба перевода «{provider}»: поддерживаются Google и DeepL.";
        return null;
    }
}
