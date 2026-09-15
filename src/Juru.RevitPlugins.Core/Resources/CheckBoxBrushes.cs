using System.Windows;
using System.Windows.Media;

using Color = System.Windows.Media.Color;

namespace Juru.RevitPlugins.Core.Resources;

/// <summary>
///     Возвращает видимую заливку отмеченному флажку под курсором и в нажатии.
/// </summary>
/// <remarks>
///     <para>
///         В WPF-UI 4.3 у отмеченного <c>CheckBox</c> заливка этих двух состояний почти прозрачна
///         (<c>#00FFFFFF</c>), а рамка отмеченного состояния прозрачна по замыслу — в сумме квадрат
///         флажка пропадает целиком, стоит навести мышь.
///     </para>
///     <para>
///         Переопределить эти кисти через словарь ресурсов не выходит: <c>ControlsDictionary</c> со
///         штатными (прозрачными) кистями попадает в <c>MergedDictionaries</c> окна последним — его
///         дописывает туда <c>ApplicationThemeManager.Apply</c>, — а при совпадении ключей побеждает
///         словарь, слитый последним. Поэтому кисти ставятся плоскими ключами прямо в
///         <see cref="FrameworkElement.Resources" />: плоский ключ старше любого смёрдженного словаря.
///     </para>
///     <para>
///         Цвета заданы числами, а не <c>{DynamicResource SystemAccentColor*}</c>: акцентные ключи
///         прописывает <c>ApplicationAccentColorManager.UpdateColorResources</c>, а он целиком обёрнут
///         в <c>if(Application.Current != null)</c>. Ревит не создаёт <c>System.Windows.Application</c>,
///         поэтому под ним эти ключи не появляются нигде и ссылка на них молча резолвится
///         в прозрачный цвет — то же самое, от чего избавлялись.
///     </para>
/// </remarks>
public static class CheckBoxBrushes
{
    private static readonly SolidColorBrush PointerOver = CreateFrozen(0x10, 0x6E, 0xBE);
    private static readonly SolidColorBrush Pressed = CreateFrozen(0x00, 0x5A, 0x9E);

    /// <summary>
    ///     Ставит кисти окну и повторяет это при его загрузке: тему окна применяют и после
    ///     конструктора (обновление темы, <c>ApplicationThemeManager.Apply</c>), а каждое такое
    ///     применение заново копирует штатные кисти плоскими ключами поверх наших.
    /// </summary>
    public static void OverrideCheckedStates(FrameworkElement element)
    {
        Override(element);
        element.Loaded += (_, _) => Override(element);
    }

    private static void Override(FrameworkElement element)
    {
        element.Resources["CheckBoxCheckBackgroundFillCheckedPointerOver"] = PointerOver;
        element.Resources["CheckBoxCheckBackgroundFillCheckedPressed"] = Pressed;
    }

    private static SolidColorBrush CreateFrozen(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();

        return brush;
    }
}
