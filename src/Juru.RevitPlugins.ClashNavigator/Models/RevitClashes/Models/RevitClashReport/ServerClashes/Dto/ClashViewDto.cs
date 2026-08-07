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
}
