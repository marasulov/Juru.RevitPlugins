using System;
using System.Globalization;
using System.Windows.Data;

// Autodesk.Revit.DB подключён неявно и тоже содержит Visibility.
using Visibility = System.Windows.Visibility;

namespace Juru.RevitPlugins.ClashNavigator.Resources;

/// <summary>
/// Двусторонне преобразует <see cref="Visibility"/> в <see cref="bool"/>.
/// Обратное направление <see cref="System.Windows.Controls.BooleanToVisibilityConverter"/>: нужно, когда
/// источник - это <see cref="Visibility"/> (например,
/// <see cref="System.Windows.Controls.DataGridColumn.Visibility"/>), а цель - <c>IsChecked</c> флажка.
/// </summary>
internal class VisibilityToBooleanConverter : IValueConverter {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
        return value is Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
        return value is true ? Visibility.Visible : Visibility.Collapsed;
    }
}
