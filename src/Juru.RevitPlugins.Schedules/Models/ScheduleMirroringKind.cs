namespace Juru.RevitPlugins.Schedules.Models;

/// <summary>
///     Способ, которым колонку придётся воспроизвести в английской спецификации.
/// </summary>
public enum ScheduleMirroringKind
{
    /// <summary>Числа, счётчики и проценты: колонка переносится без изменений.</summary>
    Untranslated,

    /// <summary>Текстовый параметр: рядом создаётся общий параметр с суффиксом En.</summary>
    Parameter,

    /// <summary>Встроенный параметр: продублировать его нельзя, перевод пишется в новый общий параметр.</summary>
    BuiltInParameter,

    /// <summary>Комбинированный параметр: пересобирается целиком вместе с префиксами и разделителями.</summary>
    CombinedParameter,

    /// <summary>
    ///     Вычисляемое значение. Строку формулы Revit API не отдаёт, поэтому в английской спецификации
    ///     формулу заводят вручную по её русскому оригиналу.
    /// </summary>
    Formula
}
