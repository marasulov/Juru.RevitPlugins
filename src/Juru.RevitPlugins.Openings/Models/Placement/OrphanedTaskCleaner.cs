using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Openings.Models.Configs;

namespace Juru.RevitPlugins.Openings.Models.Placement;

/// <summary>
/// Удаляет задания, чей исходный инженерный элемент полностью исчез из модели - не сдвинулся,
/// не вышел за габариты, а именно удалён.
/// </summary>
/// <remarks>
/// Более широкое "Неактуальное задание" (<c>OpeningTaskStatus.NotActual</c>) остаётся ручной
/// проверкой в навигаторе намеренно: труба, которая просто отошла в сторону, но всё ещё существует,
/// уже обрабатывается "обновлением на месте" (<see cref="PlacedTasksIndex.FindBySourceMep"/>) при
/// следующей расстановке - автоматически удалять её задание было бы неверно, оно ещё понадобится.
/// Здесь удаляется только тот случай, где ошибиться нельзя: у объединённого задания элемент
/// считается исчезнувшим, только если исчезли <b>все</b> его исходники - если жив хоть один,
/// отверстие всё ещё нужно.
/// </remarks>
public class OrphanedTaskCleaner {
    /// <summary>
    /// Удаляет осиротевшие задания в одной транзакции и возвращает, сколько удалено.
    /// </summary>
    public int Remove(Document doc, OpeningsConfig config) {
        if(doc is null) { throw new ArgumentNullException(nameof(doc)); }
        if(config is null) { throw new ArgumentNullException(nameof(config)); }

        var naming = config.Naming;
        var familyNames = new HashSet<string>(
            Enum.GetValues(typeof(OpeningType)).Cast<OpeningType>().Select(naming.GetFamilyName),
            StringComparer.CurrentCultureIgnoreCase);

        var instances = new FilteredElementCollector(doc)
            .OfClass(typeof(FamilyInstance))
            .WhereElementIsNotElementType()
            .Cast<FamilyInstance>()
            .Where(instance => instance.Symbol is not null && familyNames.Contains(instance.Symbol.FamilyName))
            .ToList();

        var toRemove = new List<ElementId>();
        foreach(var instance in instances) {
            // Ручное задание не связано с реальным пересечением - трогать его нельзя.
            if(instance.LookupParameter(naming.ManuallyPlacedParam)?.AsInteger() == 1) {
                continue;
            }

            string description = instance.LookupParameter(naming.DescriptionParam)?.AsString();
            var mepIds = OpeningSourceDescription.ParseMepElementIds(description);
            // Описание не разобралось (старый формат, пустое) - решить нечего, задание остаётся
            // для ручной проверки в навигаторе, как и раньше.
            if(mepIds.Count == 0) {
                continue;
            }

            bool anyAlive = mepIds.Any(id => doc.GetElement(new ElementId(id)) is not null);
            if(!anyAlive) {
                toRemove.Add(instance.Id);
            }
        }

        if(toRemove.Count == 0) {
            return 0;
        }

        using var transaction = new Transaction(doc, "Удаление заданий без исходного элемента");
        transaction.Start();
        doc.Delete(toRemove);
        transaction.Commit();

        return toRemove.Count;
    }
}
