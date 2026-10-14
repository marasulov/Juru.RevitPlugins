using System.Collections.Generic;
using System.Text.Json;

namespace Juru.RevitPlugins.ProjectSetup.Server;

// Формат заданий — копия ClashTracker/ClashTrackerApi/Setup/SetupModels.cs. Ссылки на проект API
// нет: плагин ставится отдельно от сервера. Пути в заданиях относительные, корень — UncRoot.

public sealed class ClaimedJob
{
    public long Id { get; set; }
    public string Kind { get; set; } = "";
    public string ClaimToken { get; set; } = "";
    public JsonElement Payload { get; set; }
}

public static class JobKinds
{
    public const string StageA = "revit_stage_a";
    public const string StageB = "revit_stage_b";
}

/// <summary>Что делать с файлом, который уже есть: ask — спросить через сервер, skip — оставить как есть.</summary>
public static class ConflictModes
{
    public const string Ask = "ask";
    public const string Skip = "skip";
}

public sealed class RevitFileDto
{
    public string Template { get; set; } = "";
    public string Path { get; set; } = "";
}

public sealed class BaseFileDto
{
    public long ObjectId { get; set; }
    public string Template { get; set; } = "";
    public string Path { get; set; } = "";
}

public sealed class StageAPayload
{
    public string UncRoot { get; set; } = "";
    public string OnExisting { get; set; } = ConflictModes.Ask;
    public RevitFileDto Genplan { get; set; } = new();
    public List<BaseFileDto> Bases { get; set; } = [];
}

public sealed class DisciplineFileDto
{
    public string Discipline { get; set; } = "";
    public string Template { get; set; } = "";
    public string Path { get; set; } = "";
}

public sealed class StageBObjectDto
{
    public long ObjectId { get; set; }
    public string SiteName { get; set; } = "";
    public string BasePath { get; set; } = "";
    public List<DisciplineFileDto> Files { get; set; } = [];
}

public sealed class StageBPayload
{
    public string UncRoot { get; set; } = "";
    public string OnExisting { get; set; } = ConflictModes.Ask;
    public bool AsCentral { get; set; }
    public List<StageBObjectDto> Objects { get; set; } = [];
}

/// <summary>
/// Отчёт по объекту. Сервер засчитывает только объекты, перечисленные здесь с Ok = true,
/// и только дисциплины из Created.
/// </summary>
public sealed class ObjectResult
{
    public long ObjectId { get; set; }
    public bool Ok { get; set; }
    public List<string> Created { get; set; } = [];
    public string? Error { get; set; }
}

public sealed class JobResult
{
    public List<ObjectResult> Objects { get; set; } = [];
    public List<string> Conflicts { get; set; } = [];
}

/// <summary>Итог выполнения задания для отчёта серверу: done | failed | needs_confirmation.</summary>
public sealed class JobOutcome
{
    public string Status { get; set; } = "done";
    public JobResult Result { get; set; } = new();
    public List<string> Log { get; } = [];
}
