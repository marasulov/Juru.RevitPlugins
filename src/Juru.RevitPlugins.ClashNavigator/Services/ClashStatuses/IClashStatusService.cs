using System.Collections.Generic;

using Juru.RevitPlugins.ClashNavigator.Models.Clashes;

namespace Juru.RevitPlugins.ClashNavigator.Services.ClashStatuses;

/// <summary>
/// Отправляет изменённые статусы коллизий в базу.
/// </summary>
internal interface IClashStatusService {
    /// <summary>
    /// Проверяет, можно ли отправить статус коллизии в базу.
    /// </summary>
    /// <param name="clash">Коллизия</param>
    bool CanUpdate(ClashModel clash);

    /// <summary>
    /// Отправляет новые статусы коллизий вместе с автором изменения.
    /// Коллизии, для которых <see cref="CanUpdate(ClashModel)"/> возвращает False, пропускаются.
    /// </summary>
    /// <param name="clashes">Коллизии с уже изменённым статусом</param>
    /// <returns>Результат отправки. Исключения не выбрасываются: ошибка возвращается в результате.</returns>
    ClashStatusUpdateResult UpdateStatuses(ICollection<ClashModel> clashes);
}
