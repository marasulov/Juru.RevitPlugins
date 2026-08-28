using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Juru.RevitPlugins.Core.FileNaming;
using Juru.RevitPlugins.Openings.Models.Server.Dto;

namespace Juru.RevitPlugins.Openings.Models.Server;

/// <summary>
/// Клиент REST API сервера заданий на отверстия, см. <c>docs/OpeningsApi.md</c>.
/// </summary>
/// <remarks>
/// Тот же адрес, с которого ClashNavigator читает <c>api/projects</c>, плюс эндпоинты <c>api/openings</c>.
/// Устройство клиента повторяет <c>ClashServerClient</c> - переиспользуемый <see cref="HttpClient"/> на
/// адрес сервера, прокси отключён: без этого каждый вызов заново поднимает TCP-соединение.
/// </remarks>
public class OpeningsServerClient {
    private static readonly ConcurrentDictionary<string, HttpClient> _httpClients = new();

    private static readonly JsonSerializerOptions _jsonOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _httpClient;

    public OpeningsServerClient(string baseUrl) {
        if(string.IsNullOrWhiteSpace(baseUrl)) {
            throw new ArgumentException("Не задан адрес сервера заданий на отверстия.", nameof(baseUrl));
        }

        BaseUrl = baseUrl.TrimEnd('/');
        _httpClient = _httpClients.GetOrAdd(BaseUrl, CreateHttpClient);
    }

    public string BaseUrl { get; }

    private static HttpClient CreateHttpClient(string baseUrl) {
        var handler = new HttpClientHandler {
            UseProxy = false
        };
        return new HttpClient(handler) {
            BaseAddress = new Uri(baseUrl + "/"),
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    /// <summary>
    /// Отправляет пачку выданных заданий. Повторная отправка того же <c>taskKey</c> - обновление.
    /// </summary>
    public Task<OpeningSubmitResultDto> SubmitOpeningsAsync(OpeningBatchDto batch) {
        if(batch is null) {
            throw new ArgumentNullException(nameof(batch));
        }

        return PostAsync<OpeningSubmitResultDto>("api/openings", batch);
    }

    /// <summary>
    /// Загружает задания объекта с их геометрией и обоими решениями по согласованию.
    /// </summary>
    /// <param name="disciplines">Чьи задания показать. Пусто - все дисциплины объекта.</param>
    public Task<OpeningTaskViewDto[]> GetOpeningsAsync(
        string projectCode,
        string objectName,
        IEnumerable<Discipline> disciplines = null) {

        string url = "api/openings"
            + $"?projectCode={Uri.EscapeDataString(projectCode)}"
            + $"&objectName={Uri.EscapeDataString(objectName)}";

        foreach(var discipline in disciplines ?? []) {
            url += $"&discipline={Uri.EscapeDataString(discipline.ToString())}";
        }

        return GetAsync<OpeningTaskViewDto[]>(url);
    }

    /// <summary>
    /// Отправляет решения по согласованию одной дисциплины (АР или КР).
    /// </summary>
    public Task<OpeningStatusResultDto> UpdateOpeningStatusesAsync(OpeningStatusUpdateDto request) {
        if(request is null) {
            throw new ArgumentNullException(nameof(request));
        }

        return PostAsync<OpeningStatusResultDto>("api/openings/status", request);
    }

    private Task<T> GetAsync<T>(string relativeUrl) {
        return SendAsync<T>(relativeUrl, () => _httpClient.GetAsync(relativeUrl));
    }

    private Task<T> PostAsync<T>(string relativeUrl, object body) {
        string json = JsonSerializer.Serialize(body, _jsonOptions);
        return SendAsync<T>(relativeUrl, () => _httpClient.PostAsync(
            relativeUrl,
            new StringContent(json, Encoding.UTF8, "application/json")));
    }

    private async Task<T> SendAsync<T>(string relativeUrl, Func<Task<HttpResponseMessage>> send) {
        string fullUrl = $"{BaseUrl}/{relativeUrl}";
        try {
            using var response = await send().ConfigureAwait(false);
            string content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if(!response.IsSuccessStatusCode) {
                throw new InvalidOperationException(
                    $"Сервер заданий на отверстия вернул {(int) response.StatusCode} ({response.ReasonPhrase}): {fullUrl}"
                    + (string.IsNullOrWhiteSpace(content) ? string.Empty : $". {Shorten(content)}"));
            }

            return JsonSerializer.Deserialize<T>(content, _jsonOptions);
        } catch(TaskCanceledException exception) {
            throw new TimeoutException(
                $"Сервер заданий на отверстия не ответил за {_httpClient.Timeout.TotalSeconds:0} с: {fullUrl}. "
                + "Проверьте адрес и порт в настройках плагина.", exception);
        } catch(HttpRequestException exception) {
            throw new InvalidOperationException(
                $"Не удалось обратиться к серверу заданий на отверстия: {fullUrl}. "
                + $"{exception.GetBaseException().Message}", exception);
        }
    }

    private static string Shorten(string content) {
        content = content.Trim();
        return content.Length <= 300 ? content : content.Substring(0, 300) + "...";
    }
}
