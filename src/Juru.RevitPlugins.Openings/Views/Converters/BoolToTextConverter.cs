using System.Globalization;
using System.Windows.Data;

namespace Juru.RevitPlugins.Openings.Views.Converters
{
    public class BoolToTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (bool)value ? "Rectangled" : "Circled";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (string)value == "Rectangled";
        }
    }
}
