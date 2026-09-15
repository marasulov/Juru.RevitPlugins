namespace Juru.RevitPlugins.Schedules.ViewModels;

/// <summary>
///     Строка таблицы, которую пользователь отмечает галочкой. Общий интерфейс нужен ровно затем,
///     чтобы перенос выделения грида в галочки был один на все таблицы окна, а не по одному
///     обработчику на тип строки.
/// </summary>
public interface ISelectableRow
{
    bool IsSelected { get; set; }
}
