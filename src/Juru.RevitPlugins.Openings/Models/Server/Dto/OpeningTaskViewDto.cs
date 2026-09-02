using System;
using System.Collections.Generic;

using Juru.RevitPlugins.Core.FileNaming;
using Juru.RevitPlugins.Openings.Models.Configs;

namespace Juru.RevitPlugins.Openings.Models.Server.Dto;

/// <summary>
/// Одно задание из ответа <c>GET api/openings</c> - геометрия, авторство выдачи и оба решения
/// по согласованию.
/// </summary>
public class OpeningTaskViewDto {
    public string TaskKey { get; set; }
    public OpeningType OpeningType { get; set; }
    public OpeningLocationDto Location { get; set; }
    public double? WidthMm { get; set; }
    public double? HeightMm { get; set; }
    public double? DiameterMm { get; set; }
    public double ThicknessMm { get; set; }
    public double ElevationFromZeroMm { get; set; }

    /// <summary>
    /// Поворот вокруг вертикальной оси в радианах - тот, что применён к экземпляру задания
    /// в файле-отправителе. У отверстий в перекрытии всегда 0.
    /// </summary>
    public double RotationRad { get; set; }

    public StructureKind HostKind { get; set; }
    public string LevelName { get; set; }
    public string MepSystem { get; set; }
    public MepKind? MepElementKind { get; set; }
    public string Description { get; set; }
    public bool IsUnited { get; set; }
    public Discipline Discipline { get; set; }
    public string SentBy { get; set; }
    public string SentByDisplayName { get; set; }
    public DateTime SentAt { get; set; }
    public bool Deleted { get; set; }

    /// <summary>
    /// Общие координаты файла-отправителя (этой дисциплины и объекта) изменились с прошлой
    /// отправки - см. "Fingerprint координат" в <c>docs/OpeningsApi.md</c>. Значение одно и то же
    /// у всех заданий одной дисциплины в одном ответе - это факт про файл-отправитель, не про
    /// конкретное задание.
    /// </summary>
    public bool SenderCoordinatesChanged { get; set; }

    /// <summary>
    /// Дисциплины, чьё согласование требуется по этому заданию, - то, что инженер отметил при
    /// отправке. Пусто, если сервер состав не вернул: тогда, как и раньше, требуются обе.
    /// </summary>
    public List<Discipline> Approvers { get; set; } = [];

    /// <summary>
    /// Общий статус, посчитанный сервером из <see cref="ArcDecision"/> и <see cref="StrDecision"/>:
    /// <c>sent</c>, <c>partiallyApproved</c>, <c>approved</c> или <c>rejected</c>.
    /// </summary>
    public string Status { get; set; }

    public OpeningDecisionDto ArcDecision { get; set; }
    public OpeningDecisionDto StrDecision { get; set; }
}
