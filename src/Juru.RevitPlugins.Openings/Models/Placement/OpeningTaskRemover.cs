using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

namespace Juru.RevitPlugins.Openings.Models.Placement;

/// <summary>
/// Удаляет задания, выбранные инженером в навигаторе.
/// </summary>
/// <remarks>
/// От <see cref="OrphanedTaskCleaner"/> отличается тем, кто принимает решение: там плагин сам находит
/// задания без исходного элемента, здесь удаляют то, что отметил человек, и проверять состав нечего.
/// Запись на сервере при этом не трогается напрямую: сервер считает задание удалённым, если его
/// <c>taskKey</c> не пришёл в следующей полной выгрузке (<c>scope = all</c>), и хранит вместе с
/// пометкой решение и историю - см. <c>docs/OpeningsApi.md</c>. Поэтому удаление доходит до
/// согласующих отправкой, а не отдельным запросом.
/// </remarks>
public class OpeningTaskRemover {
    /// <summary>
    /// Удаляет экземпляры заданий одной транзакцией и возвращает, сколько удалено.
    /// </summary>
    /// <remarks>
    /// Уже отсутствующие в модели элементы пропускаются: окно немодальное, и между открытием списка
    /// и нажатием кнопки задание могли удалить руками - падать из-за этого нельзя.
    /// </remarks>
    public int Remove(Document doc, IReadOnlyCollection<ElementId> ids) {
        if(doc is null) { throw new ArgumentNullException(nameof(doc)); }
        if(ids is null) { throw new ArgumentNullException(nameof(ids)); }

        var alive = ids.Where(id => doc.GetElement(id) is not null).ToList();
        if(alive.Count == 0) {
            return 0;
        }

        using var transaction = new Transaction(doc, "Удаление заданий на отверстия");
        transaction.Start();
        doc.Delete(alive);
        transaction.Commit();

        return alive.Count;
    }
}
