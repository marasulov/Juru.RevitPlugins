using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Juru.RevitPlugins.ProjectSetup.Server;

/// <summary>
/// Протокол исполнителя очереди ClashTracker: <c>api/setup/agent/claim</c> и
/// <c>api/setup/agent/jobs/{id}/complete</c>. Токен из ответа на взятие обязателен в отчёте:
/// без него сервер отчёт не примет.
/// </summary>
public sealed class SetupJobsClient : IDisposable
{
    private const string Executor = "revit";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly string _worker;

    public SetupJobsClient(string baseUrl, string worker)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ArgumentException("Не задан адрес ClashTracker (ProjectSetup.json).", nameof(baseUrl));

        BaseUrl = baseUrl.TrimEnd('/');
        _worker = worker;
        // Прокси выключен так же, как в клиентах ClashNavigator и «Отверстий»: сервер в локальной сети
        _http = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            BaseAddress = new Uri(BaseUrl + "/"),
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public string BaseUrl { get; }

    /// <summary>Адрес из ProjectSetup.json рядом со сборкой; по умолчанию — http://bimsrv/.</summary>
    public static string ReadDefaultUrl()
    {
        try
        {
            string directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            string path = Path.Combine(directory, "ProjectSetup.json");
            if (!File.Exists(path))
                return "http://bimsrv/";

            using var json = JsonDocument.Parse(File.ReadAllText(path));
            return json.RootElement.TryGetProperty("Url", out var url) && url.GetString() is { Length: > 0 } value
                ? value.Trim()
                : "http://bimsrv/";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return "http://bimsrv/";
        }
    }

    /// <returns>Задание или null, если очередь пуста.</returns>
    public async Task<ClaimedJob?> ClaimAsync()
    {
        string body = JsonSerializer.Serialize(new { executor = Executor, worker = _worker }, JsonOptions);
        using var response = await _http.PostAsync("api/setup/agent/claim", new StringContent(body, Encoding.UTF8, "application/json"))
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NoContent)
            return null;

        string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Не удалось взять задание: {(int)response.StatusCode} {text}");

        return JsonSerializer.Deserialize<ClaimedJob>(text, JsonOptions);
    }

    /// <summary>
    /// Сообщение о ходе работы для страницы проекта. Без этого задание на десяток файлов минутами
    /// висит в running, и не отличить работу от зависшего Revit. Ошибка связи работу не прерывает:
    /// отчёт о результате важнее сообщения о ходе.
    /// </summary>
    public async Task TryReportProgressAsync(ClaimedJob job, string message)
    {
        try
        {
            string body = JsonSerializer.Serialize(new { claimToken = job.ClaimToken, message }, JsonOptions);
            using var response = await _http.PostAsync($"api/setup/agent/jobs/{job.Id}/progress",
                new StringContent(body, Encoding.UTF8, "application/json")).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // сервер недоступен — продолжаем, отчёт о результате всё равно будет отправлен
        }
    }

    public async Task CompleteAsync(ClaimedJob job, JobOutcome outcome)
    {
        string body = JsonSerializer.Serialize(new
        {
            claimToken = job.ClaimToken,
            status = outcome.Status,
            log = string.Join(Environment.NewLine, outcome.Log),
            result = outcome.Result
        }, JsonOptions);

        using var response = await _http.PostAsync($"api/setup/agent/jobs/{job.Id}/complete", new StringContent(body, Encoding.UTF8, "application/json"))
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new HttpRequestException($"Сервер не принял отчёт по заданию #{job.Id}: {(int)response.StatusCode} {text}");
        }
    }

    public static T ReadPayload<T>(ClaimedJob job) => job.Payload.Deserialize<T>(JsonOptions)
        ?? throw new InvalidDataException($"Пустой план задания #{job.Id}");

    public void Dispose() => _http.Dispose();
}
