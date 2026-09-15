using System.Windows;
using System.Windows.Controls;

using dosymep.SimpleServices;

namespace Juru.RevitPlugins.ClashNavigator.Views;

/// <summary>
/// Перевод заголовков колонок таблицы.
/// </summary>
/// <remarks>
/// Разметкой это не сделать: <see cref="DataGridColumn" /> не входит в дерево элементов, поэтому
/// поиск ресурса по ключу, которым работает <c>me:LocalizationSource</c>, для него не разрешается
/// и заголовок остаётся пустым. Ключ задаётся здесь присоединённым свойством, а подстановка идёт
/// кодом, через ту же службу локализации.
/// </remarks>
internal static class ColumnLocalization {
    /// <summary>
    /// Ключ словаря локализации, которым переводится заголовок колонки.
    /// </summary>
    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key", typeof(string), typeof(ColumnLocalization), new PropertyMetadata(default(string)));

    public static void SetKey(DependencyObject element, string value) {
        element.SetValue(KeyProperty, value);
    }

    public static string GetKey(DependencyObject element) {
        return (string) element.GetValue(KeyProperty);
    }

    /// <summary>
    /// Проставляет заголовки всех колонок, у которых задан <see cref="KeyProperty" />.
    /// </summary>
    /// <param name="grid">Таблица, колонки которой переводятся.</param>
    /// <param name="localization">Служба локализации.</param>
    public static void Apply(DataGrid grid, ILocalizationService localization) {
        if(grid is null || localization is null) {
            return;
        }

        foreach(var column in grid.Columns) {
            string key = GetKey(column);
            if(string.IsNullOrEmpty(key)) {
                continue;
            }

            string title = localization.GetLocalizedString(key);
            if(!string.IsNullOrEmpty(title)) {
                column.Header = title;
            }
        }
    }
}
