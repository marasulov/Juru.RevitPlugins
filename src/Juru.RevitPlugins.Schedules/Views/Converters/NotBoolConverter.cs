using System;
using System.Globalization;
using System.Windows.Data;

namespace Juru.RevitPlugins.Schedules.Views.Converters;

/// <summary>
///     Обратное значение флага. Нужен паре переключателей, у которых состояние одно: «вся модель»
///     это просто «не только активный вид», и второго поля под это заводить незачем.
/// </summary>
public sealed class NotBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is not true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is not true;
    }
}
