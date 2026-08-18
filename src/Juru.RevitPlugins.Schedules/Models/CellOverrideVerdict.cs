namespace Juru.RevitPlugins.Schedules.Models;

/// <summary>
///     Чем закончилась запись текста в ячейку.
/// </summary>
public enum CellOverrideVerdict
{
    /// <summary>Текст лёг поверх данных, модель не затронута: подход с копированием текста работает.</summary>
    OverriddenInCopy,

    /// <summary>Текст ушёл в параметр элемента и изменил модель: подход неприменим.</summary>
    WrittenToModel,

    /// <summary>Запись отклонена Revit.</summary>
    Rejected,

    /// <summary>Запись прошла без ошибки, но текст ячейки не изменился.</summary>
    NoEffect
}
