using System;
using System.Collections.Generic;

using Juru.RevitPlugins.Core.FileNaming;

namespace Juru.RevitPlugins.Openings.Models.Server.Dto;

/// <summary>
/// Тело запроса <c>POST api/openings/status</c> - решения по согласованию одной дисциплины.
/// </summary>
public class OpeningStatusUpdateDto {
    /// <summary>
    /// <c>ARC</c> или <c>STR</c> - дисциплина файла, из которого приняты решения. Не путать с
    /// дисциплиной задания в <see cref="OpeningBatchDto.Discipline"/>.
    /// </summary>
    public Discipline Discipline { get; set; }

    public string ChangedBy { get; set; }
    public string ChangedByDisplayName { get; set; }
    public DateTime ChangedAt { get; set; }
    public string Source { get; set; } = "Revit Openings";
    public List<OpeningStatusItemDto> Items { get; set; } = [];
}

public class OpeningStatusItemDto {
    public string TaskKey { get; set; }

    /// <summary>
    /// <c>approved</c> или <c>rejected</c>.
    /// </summary>
    public string Status { get; set; }

    /// <summary>
    /// Обязателен при <c>rejected</c>.
    /// </summary>
    public string Comment { get; set; }
}
