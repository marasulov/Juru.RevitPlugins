using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading.Tasks;

using Newtonsoft.Json;

using Juru.RevitPlugins.ClashNavigator.Models.RevitClashReport.ServerClashes.Dto;

namespace Juru.RevitPlugins.ClashNavigator.Models.RevitClashReport.ServerClashes;

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
    /// Строит полный url картинки коллизии из относительного пути, возвращённого сервером.
    /// </summary>
    public string GetImageUrl(string imagePath) {
        return string.IsNullOrWhiteSpace(imagePath) ? null : $"{BaseUrl}{imagePath}";
    }

    private async Task<T> GetAsync<T>(string relativeUrl) {
        using var response = await _httpClient.GetAsync(relativeUrl).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return JsonConvert.DeserializeObject<T>(json);
    }
}
