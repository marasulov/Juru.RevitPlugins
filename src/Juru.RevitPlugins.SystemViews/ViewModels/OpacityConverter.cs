using System;
using System.Globalization;
using System.Windows.Data;

namespace Juru.RevitPlugins.SystemViews.ViewModels;

/// <summary>Приглушает строку, которой нечего добавить: <c>true</c> - полная яркость, <c>false</c> - бледная.</summary>
public sealed class OpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? 1.0 : 0.5;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
