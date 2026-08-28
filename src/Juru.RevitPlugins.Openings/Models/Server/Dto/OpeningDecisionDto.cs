using System;

namespace Juru.RevitPlugins.Openings.Models.Server.Dto;

/// <summary>
/// Решение по согласованию одной дисциплины (АР или КР) по одному заданию.
/// Пока не принято - все поля, кроме <see cref="Status"/> (<c>sent</c>), пустые.
/// </summary>
public class OpeningDecisionDto {
    /// <summary>
    /// <c>sent</c>, <c>approved</c> или <c>rejected</c>.
    /// </summary>
    public string Status { get; set; }

    public string DecidedBy { get; set; }
    public string DecidedByDisplayName { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string Comment { get; set; }

    /// <summary>
    /// Это решение только что сброшено сервером в <c>sent</c>, потому что после него инженер
    /// перевыдал задание с заметно другими габаритами или положением - решение относилось к прежней
    /// геометрии и больше не действует. Снимается само, как только по этой дисциплине примут новое
    /// решение.
    /// </summary>
    public bool ChangedAfterApproval { get; set; }
}
