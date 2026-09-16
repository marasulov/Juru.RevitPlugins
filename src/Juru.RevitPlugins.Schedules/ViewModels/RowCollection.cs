using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Juru.RevitPlugins.Schedules.ViewModels;

/// <summary>
///     Коллекция строк таблицы, которая меняется целиком за одно уведомление.
/// </summary>
/// <remarks>
///     Перечитывание модели заменяет содержимое всех таблиц окна. Через <c>Clear</c> и <c>Add</c>
///     по строке это уведомление на каждую строку, и на каждое <c>DataGrid</c> заводит контейнер
///     строки и применяет к нему шаблоны заново. Одно уведомление <c>Reset</c> вместо этого даёт
///     одну виртуализованную пересборку: контейнеры заводятся только под видимые строки.
/// </remarks>
public sealed class RowCollection<T> : ObservableCollection<T>
{
    /// <summary>Заменяет содержимое целиком.</summary>
    public void Reset(IEnumerable<T> rows)
    {
        Items.Clear();

        foreach (var row in rows)
        {
            Items.Add(row);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
