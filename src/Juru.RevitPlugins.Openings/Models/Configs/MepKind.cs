namespace Juru.RevitPlugins.Openings.Models.Configs;

/// <summary>
/// Вид инженерного элемента, для которого выдаётся задание на отверстие.
/// Настройки расстановки задаются на каждый вид отдельно.
/// </summary>
public enum MepKind {
    /// <summary>
    /// Трубы.
    /// </summary>
    Pipe,

    /// <summary>
    /// Воздуховоды круглого сечения.
    /// </summary>
    RoundDuct,

    /// <summary>
    /// Воздуховоды прямоугольного сечения.
    /// </summary>
    RectangleDuct,

    /// <summary>
    /// Кабельные лотки.
    /// </summary>
    CableTray,

    /// <summary>
    /// Кабелепроводы.
    /// </summary>
    Conduit
}
