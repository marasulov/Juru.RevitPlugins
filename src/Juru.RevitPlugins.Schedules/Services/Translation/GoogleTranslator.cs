using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Juru.RevitPlugins.Schedules.Services.Translation;

/// <summary>
///     Google Cloud Translation v2. Ключ передаётся в строке запроса, как требует эта версия API.
/// </summary>
public sealed class GoogleTranslator(HttpClient httpClient, string apiKey, TranslationSettings settings)
    : ITextTranslator
{
    private const string Endpoint = "https://translation.googleapis.com/language/translate/v2";

    public string Name
    {
        get { return "Google Cloud Translation"; }
    }

    public async Task<IReadOnlyList<string>> TranslateAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken)
    {
        var request = new
        {
            q = texts,
            source = settings.SourceLanguage,
            target = settings.TargetLanguage,
            format = "text"
        };

        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        var url = $"{Endpoint}?key={Uri.EscapeDataString(apiKey)}";

        using var response = await httpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new TranslationException($"Google ответил {(int)response.StatusCode}: {Shorten(body)}");
        }

        return ParseTranslations(body, texts.Count);
    }

    private static IReadOnlyList<string> ParseTranslations(string body, int expectedCount)
    {
        using var document = JsonDocument.Parse(body);

        if (!document.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("translations", out var translations))
        {
            throw new TranslationException("В ответе Google нет переводов.");
        }

        // Google экранирует часть символов даже при format=text.
        var texts = translations
            .EnumerateArray()
            .Select(item => WebUtility.HtmlDecode(item.GetProperty("translatedText").GetString() ?? string.Empty))
            .ToList();

        if (texts.Count != expectedCount)
        {
            throw new TranslationException($"Google вернул {texts.Count} строк вместо {expectedCount}.");
        }

        return texts;
    }

    private static string Shorten(string body)
    {
        return body.Length <= 200 ? body : $"{body[..200]}…";
    }
}
