using Autodesk.Revit.DB;

using Juru.RevitPlugins.Schedules.Models;

namespace Juru.RevitPlugins.Schedules.ViewModels;

/// <summary>
///     Всё, что окну нужно от модели. Реализует команда: окно немодальное и живёт вне контекста
///     Revit API, поэтому каждое такое действие уходит во внешнее событие.
/// </summary>
public interface ISchedulesHost
{
    /// <summary>Прочитать модель и заполнить окно.</summary>
    void Load();

    /// <summary>Открыть вид: спецификацию, лист или обычный вид.</summary>
    void ShowView(ElementId viewId);

    /// <summary>Открыть вид с элементом и выделить его.</summary>
    void ShowElement(ElementText text);

    /// <summary>Собрать английский комплект по отмеченному.</summary>
    void Build();
}
