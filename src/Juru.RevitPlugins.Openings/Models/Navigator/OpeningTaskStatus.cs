using System.ComponentModel;

using Juru.RevitPlugins.Core.Localization;

namespace Juru.RevitPlugins.Openings.Models.Navigator;

/// <summary>
/// Состояние задания на отверстие, размещённого в файле инженерных систем.
/// </summary>
/// <remarks>
/// Состояние не хранится в модели, а пересчитывается по геометрии при каждом открытии навигатора:
/// стену передвинули, воздуховод переложили - и задание, вчера корректное, сегодня неактуально.
/// Хранить такое значение означало бы показывать вчерашнюю правду. На сервере хранится только
/// решение согласующего, которое из геометрии не выводится.
/// </remarks>
public enum OpeningTaskStatus {
    /// <summary>
    /// Задание соответствует проходящему через него элементу: зазор в пределах настроек.
    /// </summary>
    [Description("Корректно")]
    Correct,

    /// <summary>
    /// Зазор вокруг элемента меньше требуемого настройками: отверстие тесное.
    /// </summary>
    [Description("Слишком маленькое задание")]
    TooSmall,

    /// <summary>
    /// Зазор вокруг элемента больше требуемого настройками: отверстие лишнее по габаритам.
    /// </summary>
    [Description("Слишком большое задание")]
    TooBig,

    /// <summary>
    /// Через задание не проходит ни один инженерный элемент, либо элемент выходит за его габариты,
    /// либо задание висит вне конструкций.
    /// </summary>
    [Description("Неактуальное задание")]
    NotActual,

    /// <summary>
    /// Задание пересекается с другим заданием этого же файла.
    /// </summary>
    [Description("Пересекающееся задание")]
    Intersects,

    /// <summary>
    /// Задание получено объединением нескольких проходов, поэтому его габариты не сверяются
    /// с одним элементом.
    /// </summary>
    [Description("Объединённое")]
    United,

    /// <summary>
    /// Задание размещено вручную: авторасстановка его не считает и не изменяет.
    /// </summary>
    [Description("Размещено вручную")]
    ManuallyPlaced,

    /// <summary>
    /// Задание пересекается с колонной или балкой: такие конструкции пробивать нельзя,
    /// решение остаётся за человеком.
    /// </summary>
    [Description("Недопустимые конструкции")]
    UnacceptableConstructions,

    /// <summary>
    /// Задание попало сразу и в стену, и в перекрытие: чинить это должен человек.
    /// </summary>
    [Description("В разных конструкциях")]
    DifferentConstructions,

    /// <summary>
    /// Нестандартное отверстие, размещённое вручную отдельным семейством: шахта или нетиповой проём.
    /// Плагин его не считает, но показывает наравне с остальными.
    /// </summary>
    [Description("Нестандартное")]
    Unique,

    /// <summary>
    /// Геометрию задания обработать не удалось.
    /// </summary>
    [Description("Ошибка обработки геометрии")]
    Invalid
}

/// <summary>
/// Названия состояний для показа пользователю.
/// </summary>
public static class OpeningTaskStatusExtensions {
    /// <summary>
    /// Возвращает название состояния для пользователя - ключ локализации "Navigator.Status.&lt;имя&gt;",
    /// определённый в assets/Localization каждого плагина, который показывает эти состояния.
    /// </summary>
    public static string GetDescription(this OpeningTaskStatus status, PluginLocalization localization) {
        return localization.GetString("Navigator.Status." + status);
    }

    /// <summary>
    /// Проверяет, требует ли состояние внимания инженера.
    /// </summary>
    /// <remarks>
    /// Порядок в навигаторе строится по этому признаку: задания, с которыми что-то не так, должны
    /// быть сверху, иначе в списке из сотни корректных проблемные не найти.
    /// </remarks>
    public static bool NeedsAttention(this OpeningTaskStatus status) {
        return status is OpeningTaskStatus.TooSmall
            or OpeningTaskStatus.TooBig
            or OpeningTaskStatus.NotActual
            or OpeningTaskStatus.Intersects
            or OpeningTaskStatus.UnacceptableConstructions
            or OpeningTaskStatus.DifferentConstructions
            or OpeningTaskStatus.Invalid;
    }
}
