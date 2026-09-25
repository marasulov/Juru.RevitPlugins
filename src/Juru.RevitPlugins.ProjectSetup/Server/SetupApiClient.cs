using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Juru.RevitPlugins.ProjectSetup.Server;

/// <summary>
/// Те же методы, что вызывает страница setup.html: проекты, объекты, запуск этапов. Закрыты
/// паролем страницы настроек ClashTracker — он хранится у пользователя (<see cref="Services.PluginSettings"/>).
/// </summary>
public sealed class SetupApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;

    public SetupApiClient(string baseUrl, string password)
    {
        BaseUrl = baseUrl.TrimEnd('/');
        _http = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            BaseAddress = new Uri(BaseUrl + "/"),
            Timeout = TimeSpan.FromSeconds(30)
        };
        _http.DefaultRequestHeaders.Add("X-Settings-Password", password ?? "");
    }

    public string BaseUrl { get; }

    public Task<SetupConfig> GetConfigAsync() => SendAsync<SetupConfig>(HttpMethod.Get, "api/setup/config");

    public Task<List<ProjectListItem>> ListProjectsAsync() => SendAsync<List<ProjectListItem>>(HttpMethod.Get, "api/setup/projects");

    public Task<ProjectDto> GetProjectAsync(long id) => SendAsync<ProjectDto>(HttpMethod.Get, $"api/setup/projects/{id}");

    public Task<ProjectDto> SaveProjectAsync(long? id, SaveProjectRequest request) =>
        id == null
            ? SendAsync<ProjectDto>(HttpMethod.Post, "api/setup/projects", request)
            : SendAsync<ProjectDto>(HttpMethod.Put, $"api/setup/projects/{id}", request);

    /// <param name="stage">stage-a или stage-b.</param>
    public Task<StageResponse> RunStageAsync(long projectId, string stage) =>
        SendAsync<StageResponse>(HttpMethod.Post, $"api/setup/projects/{projectId}/{stage}", new { objectIds = Array.Empty<long>() });

    public Task SkipExistingAsync(long jobId) =>
        SendAsync<JsonElement>(HttpMethod.Post, $"api/setup/jobs/{jobId}/resolve", new { onExisting = ConflictModes.Skip });

    private async Task<T> SendAsync<T>(HttpMethod method, string url, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body != null)
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request).ConfigureAwait(false);
        string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw SetupApiException.From(response.StatusCode, text);

        return JsonSerializer.Deserialize<T>(text, JsonOptions)!;
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>Ошибка сервера: общий текст и, если сервер их назвал, ошибки по полям.</summary>
public sealed class SetupApiException(HttpStatusCode status, string message, Dictionary<string, string> errors) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
    public Dictionary<string, string> Errors { get; } = errors;
    public bool IsUnauthorized => Status == HttpStatusCode.Unauthorized;

    public static SetupApiException From(HttpStatusCode status, string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            string message = json.RootElement.TryGetProperty("error", out var e) ? e.GetString() ?? "" : body;
            var errors = new Dictionary<string, string>();
            if (json.RootElement.TryGetProperty("errors", out var fields) && fields.ValueKind == JsonValueKind.Object)
                foreach (var field in fields.EnumerateObject())
                    errors[field.Name] = field.Value.GetString() ?? "";
            return new SetupApiException(status, message, errors);
        }
        catch (JsonException)
        {
            return new SetupApiException(status, $"{(int)status}: {body}", new());
        }
    }
}

// ---------- Ответы страницы (копия ClashTrackerApi/Setup/SetupModels.cs) ----------

public sealed class SetupConfig
{
    public List<string> Disciplines { get; set; } = [];
    public string UncRoot { get; set; } = "";
    public string BaseFileCode { get; set; } = "CRD";
    public string FileNameExample { get; set; } = "";
    public Dictionary<string, string> TemplateFolders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ProjectListItem
{
    public long Id { get; set; }
    public string FolderName { get; set; } = "";
    public string Code { get; set; } = "";
    public int Objects { get; set; }
    public int Ready { get; set; }
    public string? LastJobStatus { get; set; }

    public override string ToString() => $"{FolderName}  ({Ready}/{Objects} готово)";
}

public sealed class ProjectObjectDto
{
    public long Id { get; set; }
    public string FolderName { get; set; } = "";
    public string Code { get; set; } = "";
    public List<string> Disciplines { get; set; } = [];
    public List<string> CreatedDisciplines { get; set; } = [];
    public Dictionary<string, string> GroupOverrides { get; set; } = new();
    public string Stage { get; set; } = "draft";
}

public sealed class ProjectJobDto
{
    public long Id { get; set; }
    public string Executor { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Log { get; set; }
    public string? Progress { get; set; }
    public JsonElement? Result { get; set; }
}

public sealed class ProjectDto
{
    public long Id { get; set; }
    public string FolderName { get; set; } = "";
    public string Code { get; set; } = "";
    public string? GenplanTemplate { get; set; }
    public string? BaseTemplate { get; set; }
    public Dictionary<string, string> DisciplineTemplates { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool CreateAsCentral { get; set; }
    public string GenplanPath { get; set; } = "";
    public List<ProjectObjectDto> Objects { get; set; } = [];
    public List<ProjectJobDto> Jobs { get; set; } = [];
}

public sealed class SaveObjectRequest
{
    public long? Id { get; set; }
    public string FolderName { get; set; } = "";
    public string Code { get; set; } = "";
    public List<string> Disciplines { get; set; } = [];
    public Dictionary<string, string> GroupOverrides { get; set; } = new();
}

public sealed class SaveProjectRequest
{
    public string FolderName { get; set; } = "";
    public string Code { get; set; } = "";
    public string? GenplanTemplate { get; set; }
    public string? BaseTemplate { get; set; }
    public Dictionary<string, string> DisciplineTemplates { get; set; } = new();
    public bool CreateAsCentral { get; set; }
    public List<SaveObjectRequest> Objects { get; set; } = [];
}

public sealed class StageResponse
{
    public List<long> Jobs { get; set; } = [];
}
