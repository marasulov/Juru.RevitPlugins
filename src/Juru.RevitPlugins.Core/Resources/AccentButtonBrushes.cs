using System.Windows;
using System.Windows.Media;

using Color = System.Windows.Media.Color;

namespace Juru.RevitPlugins.Core.Resources;

/// <summary>
///     Возвращает видимую подпись главной кнопке окна (<c>Appearance="Primary"</c>).
/// </summary>
/// <remarks>
///     <para>
///         Кисти акцентной кнопки WPF-UI прописывает
///         <c>ApplicationAccentColorManager.UpdateColorResources</c>, а он целиком обёрнут
///         в <c>if(Application.Current != null)</c>. Ревит не создаёт <c>System.Windows.Application</c>,
///         поэтому под ним эти ключи не появляются нигде, ссылки на них молча резолвятся в прозрачный
///         цвет — и у кнопки пропадает подпись, пока её не нажмут. Проверено сравнением: в обычном
///         WPF-приложении та же кнопка отдаёт <c>Foreground=#FFFFFFFF</c>, <c>Background=#FF0067C0</c>.
///     </para>
///     <para>
///         Цвета заданы числами по той же причине, что и в <see cref="CheckBoxBrushes" />, и берутся
///         фирменные, а не синие по умолчанию: <c>JuruTheme.AccentColor</c> под Ревитом до кнопки
///         тоже не доходит.
///     </para>
/// </remarks>
public static class AccentButtonBrushes
{
    private static readonly SolidColorBrush Background = CreateFrozen(0xD3, 0x1B, 0x1E);
    private static readonly SolidColorBrush PointerOver = CreateFrozen(0xE0, 0x34, 0x37);
    private static readonly SolidColorBrush Pressed = CreateFrozen(0xA8, 0x15, 0x18);
    private static readonly SolidColorBrush Foreground = CreateFrozen(0xFF, 0xFF, 0xFF);

    /// <summary>
    ///     Ставит кисти окну и повторяет это при его загрузке: тему окна применяют и после
    ///     конструктора, а каждое применение копирует штатные кисти плоскими ключами поверх наших.
    /// </summary>
    public static void Override(FrameworkElement element)
    {
        Apply(element);
        element.Loaded += (_, _) => Apply(element);
    }

    private static void Apply(FrameworkElement element)
    {
        element.Resources["AccentButtonBackground"] = Background;
        element.Resources["AccentButtonBackgroundPointerOver"] = PointerOver;
        element.Resources["AccentButtonBackgroundPressed"] = Pressed;
        element.Resources["AccentButtonBorderBrushPressed"] = Pressed;

        element.Resources["AccentButtonForeground"] = Foreground;
        element.Resources["AccentButtonForegroundPointerOver"] = Foreground;
        element.Resources["AccentButtonForegroundPressed"] = Foreground;
    }

    private static SolidColorBrush CreateFrozen(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();

        return brush;
    }
}
