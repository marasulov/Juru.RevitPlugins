using System.Windows;
using System.Windows.Controls;

namespace Juru.RevitPlugins.Core.Localization;

/// <summary>
/// Ключ локализации для заголовка колонки <see cref="DataGridColumn" />.
/// </summary>
/// <remarks>
/// Колонки грида не входят в визуальное дерево окна, поэтому <c>{DynamicResource}</c> в их
/// <c>Header</c> не резолвится сам - заголовки выставляются руками через <see cref="Apply" />,
/// вызванный после <c>InitializeComponent()</c> в конструкторе окна.
/// </remarks>
public static class ColumnLocalization {
    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key", typeof(string), typeof(ColumnLocalization));

    public static void SetKey(DependencyObject element, string value) {
        element.SetValue(KeyProperty, value);
    }

    public static string GetKey(DependencyObject element) {
        return (string) element.GetValue(KeyProperty);
    }

    /// <summary>
    /// Проставляет заголовки всех колонок грида, у которых задан <see cref="KeyProperty" />.
    /// </summary>
    public static void Apply(DataGrid grid, PluginLocalization localization) {
        foreach(var column in grid.Columns) {
            string key = GetKey(column);
            if(!string.IsNullOrEmpty(key)) {
                column.Header = localization.GetString(key);
            }
        }
    }
}
