using Autodesk.Revit.UI;

using Wpf.Ui.Appearance;

using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using UiApplication = Wpf.Ui.UiApplication;

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
        EnsureHostApplication();

        ApplicationThemeManager.Apply(theme);
        ApplicationAccentColorManager.Apply(AccentColor, theme);
    }

    /// <summary>
    /// Заводит <see cref="Application"/> и резервный ресурс для иконок WPF-UI, если их ещё нет.
    /// </summary>
    /// <remarks>
    /// Ревит не создаёт <see cref="Application"/> ни для одного окна плагина. Конструктор
    /// <c>Wpf.Ui.Controls.FontIcon</c> (от него наследуется <c>SymbolIcon</c> - им рисуют галочку
    /// штатного чекбокса WPF-UI, который здесь берут под ключом <c>DefaultCheckBoxStyle</c>)
    /// читает размер шрифта иконки прямо из <c>UiApplication.Current.Resources
    /// ["DefaultIconFontSize"]</c> в момент создания. Сам WPF-UI этот ключ нигде не определяет -
    /// это точка расширения для приложения-хозяина, которую он просто не подстраховал: без
    /// значения индексатор возвращает пустую строку вместо null, и запись пустой строки в
    /// свойство FontSize (double) валится с XamlParseException прямо при открытии окна. Ключ
    /// пишется в ресурсы самого <c>UiApplication</c>, а не <see cref="Application"/> - у первого
    /// свой словарь, не всегда связанный со вторым живой ссылкой (проверено вызовом
    /// send_code_to_revit в реальном сеансе Ревита). Важен порядок: значение должно появиться до
    /// первого обращения к иконкам WPF-UI, иначе то же самое произойдёт при следующем окне.
    /// </remarks>
    private static void EnsureHostApplication() {
        if (Application.Current is null) {
            _ = new Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        }

        if (!UiApplication.Current.Resources.Contains("DefaultIconFontSize")) {
            UiApplication.Current.Resources["DefaultIconFontSize"] = 16d;
        }
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
