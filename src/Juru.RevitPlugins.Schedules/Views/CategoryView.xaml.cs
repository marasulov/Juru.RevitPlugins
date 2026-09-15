using System;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

using Juru.RevitPlugins.Schedules.ViewModels;

namespace Juru.RevitPlugins.Schedules.Views;

/// <summary>
///     Таблица одной категории модели. Категории различаются только сбором строк, поэтому таблица
///     у них общая: следующая категория — это ещё один <see cref="CategoryView" /> на своей вкладке.
/// </summary>
public partial class CategoryView : UserControl
{
    public CategoryView()
    {
        InitializeComponent();
    }

    /// <summary>
    ///     Переключает галочку строки. Щелчок гасится здесь и до таблицы не доходит.
    /// </summary>
    /// <remarks>
    ///     Переключение делается вручную, а <c>IsChecked</c> привязан односторонне: иначе
    ///     <c>ToggleButton</c> переключит значение сам, и на один щелчок придётся два изменения.
    ///     Гашение события заодно не отдаёт щелчок таблице, чтобы он не менял выделение строк.
    /// </remarks>
    private void CheckBoxCell_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (sender is not CheckBox { DataContext: ISelectableRow row })
        {
            return;
        }

        row.IsSelected = !row.IsSelected;
        eventArgs.Handled = true;
    }
}
