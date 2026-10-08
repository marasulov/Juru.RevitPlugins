using System;
using System.Globalization;
using System.Windows.Data;

namespace Juru.RevitPlugins.ClashNavigator.ViewModels.Navigator;
/// <summary>
/// Как сгруппировать строки навигатора.
/// </summary>
/// <remarks>
/// Один неудачно проложенный воздуховод даёт десятки коллизий со стенами и балками; по элементу
/// они сворачиваются в одну строку-группу, и разбирать приходится проблемы, а не пары.
/// </remarks>
internal enum ClashGrouping {
    None,
    FirstElement,
    SecondElement,
    Level,
    Status
}

/// <summary>
/// Пункт выпадающего списка группировки.
/// </summary>
internal sealed class ClashGroupingItem {
    public ClashGroupingItem(ClashGrouping grouping, string name) {
        Grouping = grouping;
        Name = name;
    }

    public ClashGrouping Grouping { get; }
    public string Name { get; }
}

/// <summary>
/// Ключ группы для строки - его же видно в заголовке группы.
/// </summary>
/// <remarks>
/// Конвертер, а не свойство строки: ключ по элементу составной (категория, имя, id, файл), и
/// добавлять его в интерфейс строки ради группировки значило бы трогать все три его реализации.
/// </remarks>
internal sealed class ClashGroupKeyConverter : IValueConverter {
    private readonly ClashGrouping _grouping;
    private readonly string _noValue;

    public ClashGroupKeyConverter(ClashGrouping grouping, string noValue) {
        _grouping = grouping;
        _noValue = noValue;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
        if(value is not IClashViewModel clash) {
            return _noValue;
        }

        string key = _grouping switch {
            ClashGrouping.FirstElement => Element(clash.FirstCategory, clash.FirstObjectName, clash.FirstId, clash.FirstDocumentName),
            ClashGrouping.SecondElement => Element(clash.SecondCategory, clash.SecondObjectName, clash.SecondId, clash.SecondDocumentName),
            ClashGrouping.Level => clash.FirstLevel,
            ClashGrouping.Status => clash.ClashStatusName,
            _ => null
        };
        return string.IsNullOrWhiteSpace(key) ? _noValue : key;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
        throw new NotSupportedException();
    }

    private static string Element(string category, string name, object id, string document) {
        return $"{category}: {name} [{id}] - {document}";
    }
}
