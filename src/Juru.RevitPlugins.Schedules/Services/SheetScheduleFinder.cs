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
    public IReadOnlyList<SheetSchedule> Find()
    {
        var placementsBySchedule = new Dictionary<ElementId, List<SheetPlacement>>();

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
