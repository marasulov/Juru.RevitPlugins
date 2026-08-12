namespace Juru.RevitPlugins.ClashNavigator.Models;

/// <summary>
/// Сохранённые между сессиями ширина и видимость колонки таблицы навигатора коллизий.
/// </summary>
internal class ClashColumnSettings {
    /// <summary>
    /// Ширина колонки: доля для звёздочной ширины (<see cref="IsStarWidth"/>) или пиксели.
    /// Значение 0 или меньше означает "оставить ширину по умолчанию".
    /// </summary>
    public double Width { get; set; }

    /// <summary>
    /// Ширина задана в звёздочках (доля свободного места), а не в пикселях.
    /// </summary>
    public bool IsStarWidth { get; set; }

    public bool IsVisible { get; set; } = true;
}
