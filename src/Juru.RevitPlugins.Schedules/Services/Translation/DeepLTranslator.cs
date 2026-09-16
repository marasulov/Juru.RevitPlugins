using System.Collections.Generic;
using System.Linq;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Juru.RevitPlugins.Schedules.Services.Translation;

/// <summary>
///     DeepL. На технических текстах точнее общего машинного перевода, поэтому годится там, где важна
///     терминология. Бесплатный ключ работает на отдельном адресе.
/// </summary>
public sealed class DeepLTranslator(HttpClient httpClient, string apiKey, TranslationSettings settings)
    : ITextTranslator
{
    private const string FreeEndpoint = "https://api-free.deepl.com/v2/translate";
    private const string PaidEndpoint = "https://api.deepl.com/v2/translate";
    private const string FreeKeySuffix = ":fx";

    public string Name
    {
        get { return "DeepL"; }
    }

    public async Task<IReadOnlyList<string>> TranslateAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken)
    {
        var request = new
        {
            text = texts,
            source_lang = settings.SourceLanguage.ToUpperInvariant(),
            target_lang = settings.TargetLanguage.ToUpperInvariant()
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, ResolveEndpoint())
        {
            Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json")
        };

        message.Headers.TryAddWithoutValidation("Authorization", $"DeepL-Auth-Key {apiKey}");

        // Сеть, прокси и таймаут бросают свои исключения, а не наш TranslationException. Наружу их
        // выпускать нельзя: сбой перевода - ожидаемый исход, о котором окно сообщает строкой, тогда
        // как чужое исключение из асинхронной команды роняет само окно.
        HttpResponseMessage response;
        string body;

        try
        {
            response = await httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
            body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new TranslationException($"DeepL недоступен: {TranslationFailure.Describe(exception)}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TranslationException($"DeepL не ответил за {httpClient.Timeout.TotalSeconds:0} с.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new TranslationException($"DeepL ответил {(int)response.StatusCode}: {Shorten(body)}");
            }
        }

        return ParseTranslations(body, texts.Count);
    }

    private string ResolveEndpoint()
    {
        return apiKey.EndsWith(FreeKeySuffix, System.StringComparison.Ordinal) ? FreeEndpoint : PaidEndpoint;
    }

    private static IReadOnlyList<string> ParseTranslations(string body, int expectedCount)
    {
        // Вместо ответа службы приходит и страница прокси, и сообщение шлюза: разбор такого текста
        // падает, и падение это тоже про недоступность службы, а не про ошибку в коде.
        using var document = TranslationFailure.Parse(body, "DeepL");

        if (!document.RootElement.TryGetProperty("translations", out var translations))
        {
            throw new TranslationException("В ответе DeepL нет переводов.");
        }

        var texts = translations
            .EnumerateArray()
            .Select(item => item.GetProperty("text").GetString() ?? string.Empty)
            .ToList();

        if (texts.Count != expectedCount)
        {
            throw new TranslationException($"DeepL вернул {texts.Count} строк вместо {expectedCount}.");
        }

        return texts;
    }

    private static string Shorten(string body)
    {
        return body.Length <= 200 ? body : $"{body[..200]}…";
    }
}
