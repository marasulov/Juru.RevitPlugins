using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using Newtonsoft.Json;

using ClashNavigator.Server.Dto;

namespace ClashNavigator.Server;

/// <summary>
/// Клиент REST API сервера коллизий.
/// </summary>
internal class ClashServerClient {
    // HttpClient и его пул соединений переиспользуются между вызовами (по адресу сервера).
    // Если создавать новый HttpClient на каждый клик "Загрузить с сервера", каждый запрос
    // заново устанавливает TCP-соединение (антивирус/фаервол инспектируют новые соединения),
    // из-за чего задержка не уходит даже при повторных загрузках.
    private static readonly ConcurrentDictionary<string, HttpClient> _httpClients = new();

    private readonly HttpClient _httpClient;

    public ClashServerClient(string baseUrl) {
        if(string.IsNullOrWhiteSpace(baseUrl)) {
            throw new ArgumentException("Не задан адрес сервера коллизий.", nameof(baseUrl));
        }

        BaseUrl = baseUrl.TrimEnd('/');
        _httpClient = _httpClients.GetOrAdd(BaseUrl, CreateHttpClient);
    }

    private static HttpClient CreateHttpClient(string baseUrl) {
        // По умолчанию HttpClientHandler пытается определить системный прокси (WinINet/WPAD)
        // перед каждым запросом - для localhost это лишняя и заметная задержка.
        var handler = new HttpClientHandler {
            UseProxy = false
        };
        return new HttpClient(handler) {
            BaseAddress = new Uri(baseUrl + "/"),
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    public string BaseUrl { get; }

    public Task<ProjectDto[]> GetProjectsAsync() {
        return GetAsync<ProjectDto[]>("api/projects");
    }

    public Task<ObjectSummaryDto[]> GetObjectsAsync(long projectId) {
        return GetAsync<ObjectSummaryDto[]>($"api/projects/{projectId}/objects");
    }

    public Task<ClashViewDto[]> GetClashesAsync(long objectId, string status = null) {
        string url = $"api/objects/{objectId}/clashes";
        if(!string.IsNullOrWhiteSpace(status)) {
            url += $"?status={Uri.EscapeDataString(status)}";
        }
        return GetAsync<ClashViewDto[]>(url);
    }

    /// <summary>
    /// Отправляет на сервер новые статусы коллизий вместе с автором изменения.
    /// </summary>
    /// <param name="request">Изменённые коллизии и автор изменения</param>
    /// <returns>Результат записи в базу</returns>
    public Task<ClashStatusUpdateResultDto> UpdateClashStatusesAsync(ClashStatusUpdateDto request) {
        if(request is null) {
            throw new ArgumentNullException(nameof(request));
        }

        return PostAsync<ClashStatusUpdateResultDto>("api/clashes/status", request);
    }

    /// <summary>
    /// Строит полный url картинки коллизии из относительного пути, возвращённого сервером.
    /// </summary>
    public string GetImageUrl(string imagePath) {
        return string.IsNullOrWhiteSpace(imagePath) ? null : $"{BaseUrl}{imagePath}";
    }

    private Task<T> GetAsync<T>(string relativeUrl) {
        return SendAsync<T>(relativeUrl, () => _httpClient.GetAsync(relativeUrl));
    }

    private Task<T> PostAsync<T>(string relativeUrl, object body) {
        string json = JsonConvert.SerializeObject(body);
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
                    $"Сервер коллизий вернул {(int) response.StatusCode} ({response.ReasonPhrase}): {fullUrl}"
                    + (string.IsNullOrWhiteSpace(content) ? string.Empty : $". {Shorten(content)}"));
            }

            return JsonConvert.DeserializeObject<T>(content);
        } catch(TaskCanceledException exception) {
            // HttpClient сообщает о собственном таймауте через TaskCanceledException.
            throw new TimeoutException(
                $"Сервер коллизий не ответил за {_httpClient.Timeout.TotalSeconds:0} с: {fullUrl}. "
                + "Проверьте адрес и порт в настройках плагина.", exception);
        } catch(HttpRequestException exception) {
            throw new InvalidOperationException(
                $"Не удалось обратиться к серверу коллизий: {fullUrl}. "
                + $"{exception.GetBaseException().Message}", exception);
        }
    }

    /// <summary>
    /// Обрезает тело ответа: в сообщение об ошибке попадает только начало,
    /// иначе страница ошибки сервера целиком уезжает в диалог пользователя.
    /// </summary>
    private static string Shorten(string content) {
        content = content.Trim();
        return content.Length <= 300 ? content : content.Substring(0, 300) + "...";
    }
}
