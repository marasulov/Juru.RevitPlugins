namespace Juru.RevitPlugins.Openings.Models.Server.Dto;

/// <summary>
/// Точка вставки задания в координатах проекта отправителя, в метрах.
/// </summary>
public class OpeningLocationDto {
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
}
