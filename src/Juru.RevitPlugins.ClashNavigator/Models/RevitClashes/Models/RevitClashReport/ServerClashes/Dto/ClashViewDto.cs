using System;

namespace Juru.RevitPlugins.ClashNavigator.Models.RevitClashReport.ServerClashes.Dto;
internal class ClashViewDto {
    public long Id { get; set; }
    public string ProjectName { get; set; }
    public string ObjectName { get; set; }
    public string ClashTestName { get; set; }
    public string Status { get; set; }
    public string ObjectAName { get; set; }
    public string ObjectBName { get; set; }
    /// <summary>
    /// Id элемента в Revit в виде строки. Пустая строка, если Navisworks не смог определить Element ID
    /// (например, нативная геометрия Navisworks без связи с Revit-элементом).
    /// </summary>
    public string ElementIdA { get; set; }
    public string ElementIdB { get; set; }
    /// <summary>
    /// Дисциплина элемента A (ARC, STR, WSS, ELE, HVC, FAD). По ней определяется файл,
    /// в котором нужно искать элемент: Id элемента уникален внутри файла, а не между файлами.
    /// </summary>
    public string DisciplineA { get; set; }
    public string DisciplineB { get; set; }
    public decimal? CoordX { get; set; }
    public decimal? CoordY { get; set; }
    public decimal? CoordZ { get; set; }
    public decimal? Distance { get; set; }
    public string GridLocation { get; set; }
    public string AssignedTo { get; set; }
    public string Description { get; set; }
    public string ImagePath { get; set; }
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    /// <summary>
    /// Учётная запись, изменившая статус последней. Null, если статус ни разу не меняли из Ревита.
    /// </summary>
    public string StatusChangedBy { get; set; }
    /// <summary>
    /// Имя сотрудника, изменившего статус ("Yusuf Marasulov"). Null, если сервер его не хранит:
    /// тогда в таблице показывается учётная запись из <see cref="StatusChangedBy"/>.
    /// </summary>
    public string StatusChangedByName { get; set; }
    public DateTime? StatusChangedAt { get; set; }
}
