using System;
using System.Collections.Generic;
using System.Linq;

using Juru.RevitPlugins.Schedules.Models;

namespace Juru.RevitPlugins.Schedules.Services;

/// <summary>
///     Сводит исходные строки всех спецификаций в один список: заготовка глоссария для переводчика.
/// </summary>
public static class GlossarySeedBuilder
{
    public static IReadOnlyList<GlossarySeedEntry> Build(ScheduleAuditReport report)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        var collected = new Dictionary<(string SourceText, GlossarySeedOrigin Origin), SortedSet<string>>();

        foreach (var schedule in report.Schedules)
        {
            foreach (var field in schedule.Fields)
            {
                var usage = $"{schedule.ScheduleName} → {field.FieldName}";

                Add(collected, field.ColumnHeading, GlossarySeedOrigin.ColumnHeading, usage);

                foreach (var literal in field.CombinedParameterLiterals)
                {
                    Add(collected, literal, GlossarySeedOrigin.CombinedParameterLiteral, usage);
                }

                if (!field.RequiresTranslation)
                {
                    continue;
                }

                foreach (var value in field.DistinctValues)
                {
                    Add(collected, value, GlossarySeedOrigin.CellValue, usage);
                }
            }

            foreach (var filter in schedule.Filters)
            {
                Add(collected, filter.StringValue, GlossarySeedOrigin.FilterValue, $"{schedule.ScheduleName} → фильтр {filter.FieldName}");
            }
        }

        return collected
            .Select(pair => new GlossarySeedEntry
            {
                SourceText = pair.Key.SourceText,
                Origin = pair.Key.Origin,
                Usages = pair.Value.ToList()
            })
            .OrderBy(entry => entry.Origin)
            .ThenBy(entry => entry.SourceText, StringComparer.CurrentCulture)
            .ToList();
    }

    private static void Add(
        Dictionary<(string SourceText, GlossarySeedOrigin Origin), SortedSet<string>> collected,
        string? sourceText,
        GlossarySeedOrigin origin,
        string usage)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return;
        }

        var key = (sourceText!.Trim(), origin);

        if (!collected.TryGetValue(key, out var usages))
        {
            usages = new SortedSet<string>(StringComparer.CurrentCulture);
            collected[key] = usages;
        }

        usages.Add(usage);
    }
}
