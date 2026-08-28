using System;
using System.Collections.Generic;

using Juru.RevitPlugins.Core.FileNaming;

namespace Juru.RevitPlugins.Openings.Models.Server.Dto;

/// <summary>
/// Тело запроса <c>POST api/openings</c> - пачка выданных заданий одной дисциплины.
/// </summary>
public class OpeningBatchDto {
    public string ProjectCode { get; set; }
    public string ObjectName { get; set; }
    public Discipline Discipline { get; set; }
    public string SentBy { get; set; }
    public string SentByDisplayName { get; set; }
    public DateTime SentAt { get; set; }
    public string Source { get; set; } = "Revit Openings";
    public string WipFilePath { get; set; }

    /// <summary>
    /// <c>all</c> - пачка содержит все задания файла, <c>partial</c> - только выбранные элементы.
    /// </summary>
    public string Scope { get; set; }

    /// <summary>
    /// Дисциплины, чьё согласование требуется по этим заданиям: <c>ARC</c>, <c>STR</c> или обе.
    /// Выбирает инженер при отправке, и от этого зависит общий статус задания на сервере -
    /// см. "С кем согласовывать" в <c>docs/OpeningsApi.md</c>.
    /// </summary>
    public List<Discipline> Approvers { get; set; } = [];

    public List<OpeningItemDto> Items { get; set; } = [];

    /// <summary>
    /// Отпечаток общих координат файла-отправителя на момент отправки. <c>null</c>, если
    /// прочитать не удалось - тогда сервер сравнение пропускает, а не считает расхождением.
    /// </summary>
    public OpeningCoordinateFingerprintDto CoordinateFingerprint { get; set; }
}
