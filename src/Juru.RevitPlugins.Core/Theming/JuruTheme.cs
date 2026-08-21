using Color = System.Windows.Media.Color;

using Wpf.Ui.Appearance;

namespace Juru.RevitPlugins.Core.Theming;

/// <summary>
/// Фирменное оформление плагинов.
/// </summary>
/// <remarks>
/// Цвет взят из иконок на ленте (#D31B1E) - до этого он существовал только в png, а окна брали
/// синий акцент WPF-UI по умолчанию, из-за чего выглядели чужими.
/// </remarks>
public static class JuruTheme {
    /// <summary>
    /// Фирменный цвет: им подсвечиваются кнопки, флажки и выделение в списках.
    /// </summary>
    public static Color AccentColor { get; } = Color.FromRgb(0xD3, 0x1B, 0x1E);

    /// <summary>
    /// Применяет фирменный акцент к окнам WPF-UI.
    /// </summary>
    /// <param name="theme">Светлая или тёмная тема окон.</param>
    public static void Apply(ApplicationTheme theme = ApplicationTheme.Light) {
        ApplicationAccentColorManager.Apply(AccentColor, theme);
    }
}
