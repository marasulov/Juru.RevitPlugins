using System;
using System.Collections;
using System.Globalization;
using System.Windows.Data;

namespace Juru.RevitPlugins.ClashNavigator.Resources;

/// <summary>
/// Возвращает значение словаря по ключу: values[0] — <see cref="IDictionary"/>, values[1] — ключ.
/// Используется, чтобы обратиться к элементу словаря по ключу, известному только в момент биндинга (например, из Tag заголовка колонки).
/// </summary>
internal class DictionaryIndexerConverter : IMultiValueConverter {
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
        if(values is not [IDictionary dictionary, string key] || key is null) {
            return null;
        }

        return dictionary.Contains(key) ? dictionary[key] : null;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) {
        throw new NotSupportedException();
    }
}
