using Juru.RevitPlugins.Openings.Models.Configs;

namespace Juru.RevitPlugins.Openings.Models.Server.Dto;

/// <summary>
/// Одно выдаваемое задание в пачке <see cref="OpeningBatchDto"/>.
/// </summary>
public class OpeningItemDto {
    public string TaskKey { get; set; }
    public OpeningType OpeningType { get; set; }
    public OpeningLocationDto Location { get; set; }
    public double? WidthMm { get; set; }
    public double? HeightMm { get; set; }
    public double? DiameterMm { get; set; }
    public double ThicknessMm { get; set; }
    public string MepSystem { get; set; }
    public MepKind? MepElementKind { get; set; }
    public StructureKind HostKind { get; set; }
    public string LevelName { get; set; }
    public double ElevationFromZeroMm { get; set; }

    /// <summary>
    /// Поворот вокруг вертикальной оси в радианах - тот, что применён к экземпляру задания
    /// в файле-отправителе. У отверстий в перекрытии всегда 0.
    /// </summary>
    public double RotationRad { get; set; }

    public string Description { get; set; }
    public bool ManuallyPlaced { get; set; }

    /// <summary>
    /// Задание получено объединением нескольких проходов в файле-отправителе - геометрически
    /// это неотличимо на приёмной стороне, поэтому флаг передаётся, а не пересчитывается.
    /// </summary>
    public bool IsUnited { get; set; }
}
