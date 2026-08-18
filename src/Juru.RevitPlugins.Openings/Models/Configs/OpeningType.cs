namespace Juru.RevitPlugins.Openings.Models.Configs;

/// <summary>
/// Тип отверстия: сочетание конструкции и формы.
/// Определяет, какое семейство ставится и как считается точка вставки.
/// </summary>
public enum OpeningType {
    /// <summary>
    /// Круглое отверстие в стене.
    /// </summary>
    WallRound,

    /// <summary>
    /// Прямоугольное отверстие в стене.
    /// </summary>
    WallRectangle,

    /// <summary>
    /// Круглое отверстие в перекрытии.
    /// </summary>
    FloorRound,

    /// <summary>
    /// Прямоугольное отверстие в перекрытии.
    /// </summary>
    FloorRectangle
}
