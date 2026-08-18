using System;

namespace Juru.RevitPlugins.Schedules.Models;

/// <summary>
///     Попытка записать перевод в одну ячейку копии спецификации.
/// </summary>
[Serializable]
public sealed record CellOverrideProbeAttempt
{
    public required int Row { get; init; }
    public required int Column { get; init; }
    public required string TextBefore { get; init; }
    public required string TextAfter { get; init; }

    /// <summary>Считает ли Revit ячейку переопределённой после записи.</summary>
    public required bool IsCellOverridden { get; init; }

    /// <summary>
    ///     Изменился ли текст той же ячейки в исходной спецификации. Обе показывают одни и те же элементы,
    ///     поэтому изменение означает, что запись ушла в модель, а не осталась оформлением копии.
    /// </summary>
    public required bool SourceScheduleChanged { get; init; }

    public string? Error { get; init; }

    public required CellOverrideVerdict Verdict { get; init; }
}
