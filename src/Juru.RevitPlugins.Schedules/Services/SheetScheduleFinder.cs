using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

namespace Juru.RevitPlugins.Schedules.Services;

/// <summary>
///     Находит спецификации, размещённые на листах: именно они составляют выпускаемый комплект.
/// </summary>
public sealed class SheetScheduleFinder(Document document)
{
    /// <param name="sheetId">
    ///     Лист, к которому сузить поиск, или <c>null</c> для всех листов. Спецификация попадает в
    ///     результат, только если у неё есть размещение на этом листе - остальные её размещения тоже
    ///     показаны, чтобы отчёт был честным о том, где ещё она встречается.
    /// </param>
    public IReadOnlyList<SheetSchedule> Find(ElementId? sheetId = null)
    {
        var placementsBySchedule = new Dictionary<ElementId, List<SheetPlacement>>();
        var scheduleIdsOnSheet = new HashSet<ElementId>();

        var instances = new FilteredElementCollector(document)
            .OfClass(typeof(ScheduleSheetInstance))
            .Cast<ScheduleSheetInstance>()
            .Where(instance => !instance.IsTitleblockRevisionSchedule);

        foreach (var instance in instances)
        {
            if (document.GetElement(instance.OwnerViewId) is not ViewSheet sheet)
            {
                continue;
            }

            if (sheetId is not null && sheet.Id == sheetId)
            {
                scheduleIdsOnSheet.Add(instance.ScheduleId);
            }

            if (!placementsBySchedule.TryGetValue(instance.ScheduleId, out var placements))
            {
                placements = [];
                placementsBySchedule[instance.ScheduleId] = placements;
            }

            placements.Add(new SheetPlacement(sheet.Id, sheet.SheetNumber, instance.Point));
        }

        var found = new List<SheetSchedule>();

        foreach (var pair in placementsBySchedule)
        {
            if (sheetId is not null && !scheduleIdsOnSheet.Contains(pair.Key))
            {
                continue;
            }

            if (document.GetElement(pair.Key) is not ViewSchedule schedule)
            {
                continue;
            }

            var ordered = pair.Value
                .OrderBy(placement => placement.SheetNumber, StringComparer.CurrentCulture)
                .ToList();

            found.Add(new SheetSchedule(schedule, ordered));
        }

        return found
            .OrderBy(entry => entry.Schedule.Name, StringComparer.CurrentCulture)
            .ToList();
    }
}

/// <summary>Спецификация вместе со всеми её размещениями на листах.</summary>
public sealed record SheetSchedule(ViewSchedule Schedule, IReadOnlyList<SheetPlacement> Placements);

/// <summary>Размещение спецификации на листе; точка привязана к левому верхнему углу таблицы.</summary>
public sealed record SheetPlacement(ElementId SheetId, string SheetNumber, XYZ Point);

/// <summary>
///     Прочитанная спецификация: снимок таблицы и всё, что уже известно для отчёта. Снимок пуст, если
///     чтение не удалось.
/// </summary>
public sealed record ReadSchedule(
    SheetSchedule Source,
    Juru.RevitPlugins.Schedules.Models.ScheduleTableSnapshot? Snapshot,
    Juru.RevitPlugins.Schedules.Models.SheetScheduleCopy Described);
