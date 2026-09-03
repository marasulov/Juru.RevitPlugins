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
    private readonly ConcurrentDictionary<string, (string ProjectCode, string ObjectName)> _resolvedNames = new();

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
    /// <remarks>
    /// Имена проекта и объекта перед отправкой приводятся к тем, что стоят в базе сервера:
    /// иначе <c>Dormitory_building</c> из имени файла заводит на сервере объект-двойник рядом
    /// с <c>Dormitory building</c>, и отправленных заданий не видит ни фронт, ни соседний плагин.
    /// </remarks>
    public async Task<OpeningSubmitResultDto> SubmitOpeningsAsync(OpeningBatchDto batch) {
        if(batch is null) {
            throw new ArgumentNullException(nameof(batch));
        }

        var names = await ResolveNamesAsync(batch.ProjectCode, batch.ObjectName).ConfigureAwait(false);
        batch.ProjectCode = names.ProjectCode;
        batch.ObjectName = names.ObjectName;

        return await PostAsync<OpeningSubmitResultDto>("api/openings", batch).ConfigureAwait(false);
    }

    /// <summary>
    /// Загружает задания объекта с их геометрией и обоими решениями по согласованию.
    /// </summary>
    /// <param name="disciplines">Чьи задания показать. Пусто - все дисциплины объекта.</param>
    public async Task<OpeningTaskViewDto[]> GetOpeningsAsync(
        string projectCode,
        string objectName,
        IEnumerable<Discipline> disciplines = null) {

        var names = await ResolveNamesAsync(projectCode, objectName).ConfigureAwait(false);

        string url = "api/openings"
            + $"?projectCode={Uri.EscapeDataString(names.ProjectCode)}"
            + $"&objectName={Uri.EscapeDataString(names.ObjectName)}";

        foreach(var discipline in disciplines ?? []) {
            url += $"&discipline={Uri.EscapeDataString(discipline.ToString())}";
        }

        return await GetAsync<OpeningTaskViewDto[]>(url).ConfigureAwait(false);
    }

    /// <summary>
    /// Проекты справочника сервера - те же, что читает ClashNavigator.
    /// </summary>
    public Task<ServerProjectDto[]> GetProjectsAsync() {
        return GetAsync<ServerProjectDto[]>("api/projects");
    }

    /// <summary>
    /// Объекты проекта.
    /// </summary>
    public Task<ServerObjectDto[]> GetObjectsAsync(int projectId) {
        return GetAsync<ServerObjectDto[]>($"api/projects/{projectId}/objects");
    }

    /// <summary>
    /// Возвращает имена проекта и объекта так, как они записаны в базе сервера: в имени файла
    /// пробел пишут подчёркиванием (<c>Dormitory_building</c>), а объект на сервере называется
    /// <c>Dormitory building</c> - см. <see cref="ServerNames"/>.
    /// </summary>
    /// <remarks>
    /// Ответ справочника кэшируется на время жизни клиента: имена нужны каждому вызову, а меняются
    /// они реже, чем идёт работа. Если справочник недоступен или имени в нём нет - возвращаются
    /// имена из файла: сорвать из-за этого отправку хуже, чем отправить как раньше.
    /// </remarks>
    private async Task<(string ProjectCode, string ObjectName)> ResolveNamesAsync(
        string projectCode,
        string objectName) {

        string key = projectCode + "|" + objectName;
        if(_resolvedNames.TryGetValue(key, out var cached)) {
            return cached;
        }

        var resolved = (ProjectCode: projectCode, ObjectName: objectName);
        try {
            var projects = await GetProjectsAsync().ConfigureAwait(false);
            var project = projects?.FirstOrDefault(item => ServerNames.Match(item.Name, projectCode));
            if(project is not null) {
                resolved.ProjectCode = project.Name;

                var objects = await GetObjectsAsync(project.Id).ConfigureAwait(false);
                var found = objects?.FirstOrDefault(item => ServerNames.Match(item.Name, objectName));
                if(found is not null) {
                    resolved.ObjectName = found.Name;
                }
            }
        } catch(Exception exception) when(exception is InvalidOperationException or TimeoutException) {
            // Справочник не ответил - работаем с именами из файла, как работали раньше.
        }

        _resolvedNames[key] = resolved;
        return resolved;
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
