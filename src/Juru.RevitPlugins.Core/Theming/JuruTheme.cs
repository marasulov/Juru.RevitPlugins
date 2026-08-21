using Autodesk.Revit.UI;

using Wpf.Ui.Appearance;

using Color = System.Windows.Media.Color;

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
    /// Применяет фирменное оформление, подхватывая светлую или тёмную тему из настроек Revit.
    /// </summary>
    public static void Apply() {
        Apply(GetRevitTheme());
    }

    /// <summary>
    /// Применяет фирменное оформление с заданной темой.
    /// </summary>
    public static void Apply(ApplicationTheme theme) {
        ApplicationThemeManager.Apply(theme);
        ApplicationAccentColorManager.Apply(AccentColor, theme);
    }

    /// <summary>
    /// Возвращает тему, выбранную в настройках интерфейса Revit.
    /// </summary>
    /// <remarks>
    /// Берём тему интерфейса, а не тему области рисования: окна плагина стоят рядом с панелями
    /// Revit, а не с моделью, и должны совпадать с ними.
    /// </remarks>
    private static ApplicationTheme GetRevitTheme() {
        return UIThemeManager.CurrentTheme == UITheme.Dark
            ? ApplicationTheme.Dark
            : ApplicationTheme.Light;
    }
}
