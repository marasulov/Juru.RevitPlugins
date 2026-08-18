using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Schedules.Models;

using RevitException = Autodesk.Revit.Exceptions.ApplicationException;

namespace Juru.RevitPlugins.Schedules.Services;

/// <summary>
///     Разбирает спецификации модели: из каких параметров состоит каждая колонка и какие значения в ней
///     встречаются. Модель не изменяется.
/// </summary>
public sealed class ScheduleAuditService(Document document)
{
    private const int MaxReportedValuesPerField = 200;

    public ScheduleAuditReport Audit()
    {
        var sheetPlacements = CountSheetPlacements();

        var schedules = new FilteredElementCollector(document)
            .OfClass(typeof(ViewSchedule))
            .Cast<ViewSchedule>()
            .Where(IsAuditable)
            .OrderBy(schedule => schedule.Name, StringComparer.CurrentCulture)
            .Select(schedule => AuditSchedule(schedule, sheetPlacements))
            .ToList();

        return new ScheduleAuditReport
        {
            ModelName = document.Title,
            CreatedAt = DateTime.Now,
            Schedules = schedules
        };
    }

    private static bool IsAuditable(ViewSchedule schedule)
    {
        if (schedule.IsTemplate)
        {
            return false;
        }

        if (schedule.IsTitleblockRevisionSchedule)
        {
            return false;
        }

        if (schedule.IsInternalKeynoteSchedule)
        {
            return false;
        }

        return true;
    }

    private Dictionary<ElementId, int> CountSheetPlacements()
    {
        var placements = new Dictionary<ElementId, int>();

        var instances = new FilteredElementCollector(document)
            .OfClass(typeof(ScheduleSheetInstance))
            .Cast<ScheduleSheetInstance>();

        foreach (var instance in instances)
        {
            placements.TryGetValue(instance.ScheduleId, out var count);
            placements[instance.ScheduleId] = count + 1;
        }

        return placements;
    }

    private ScheduleAudit AuditSchedule(ViewSchedule schedule, IReadOnlyDictionary<ElementId, int> sheetPlacements)
    {
        var definition = schedule.Definition;

        var fields = definition.GetFieldOrder()
            .Select(definition.GetField)
            .ToList();

        var cellValues = HarvestCellValues(schedule, fields, out var warning);
        var valuesHarvested = warning is null;

        var fieldAudits = fields
            .Select((field, position) => AuditField(field, position, cellValues, valuesHarvested))
            .ToList();

        sheetPlacements.TryGetValue(schedule.Id, out var placementCount);

        return new ScheduleAudit
        {
            ScheduleName = schedule.Name,
            CategoryName = ResolveCategoryName(definition),
            IsKeySchedule = definition.IsKeySchedule,
            SheetPlacementCount = placementCount,
            Fields = fieldAudits,
            SortGroupFieldNames = ResolveSortGroupFieldNames(definition),
            Filters = AuditFilters(definition),
            ValueHarvestWarning = warning
        };
    }

    private string ResolveCategoryName(ScheduleDefinition definition)
    {
        try
        {
            var category = Category.GetCategory(document, definition.CategoryId);
            return category?.Name ?? MultiCategoryName;
        }
        catch (RevitException)
        {
            return MultiCategoryName;
        }
    }

    private const string MultiCategoryName = "<несколько категорий>";

    private static IReadOnlyList<string> ResolveSortGroupFieldNames(ScheduleDefinition definition)
    {
        var names = new List<string>();

        foreach (var sortGroupField in definition.GetSortGroupFields())
        {
            var name = TryGetFieldName(definition, sortGroupField.FieldId);
            if (name is not null)
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static IReadOnlyList<ScheduleFilterAudit> AuditFilters(ScheduleDefinition definition)
    {
        var filters = new List<ScheduleFilterAudit>();

        foreach (var filter in definition.GetFilters())
        {
            filters.Add(new ScheduleFilterAudit
            {
                FieldName = TryGetFieldName(definition, filter.FieldId) ?? "<поле недоступно>",
                FilterType = filter.FilterType.ToString(),
                StringValue = filter.IsStringValue ? filter.GetStringValue() : null
            });
        }

        return filters;
    }

    private static string? TryGetFieldName(ScheduleDefinition definition, ScheduleFieldId fieldId)
    {
        try
        {
            return definition.GetField(fieldId).GetName();
        }
        catch (RevitException)
        {
            return null;
        }
    }

    /// <summary>
    ///     Собирает напечатанный текст ячеек по колонкам. Колонки тела таблицы соответствуют видимым полям
    ///     в порядке следования; при расхождении значения не собираются.
    /// </summary>
    private static Dictionary<int, HashSet<string>> HarvestCellValues(
        ViewSchedule schedule,
        IReadOnlyList<ScheduleField> fields,
        out string? warning)
    {
        warning = null;
        var values = new Dictionary<int, HashSet<string>>();

        var visiblePositions = new List<int>();
        for (var position = 0; position < fields.Count; position++)
        {
            if (!fields[position].IsHidden)
            {
                visiblePositions.Add(position);
            }
        }

        try
        {
            var body = schedule.GetTableData().GetSectionData(SectionType.Body);

            if (body.NumberOfColumns != visiblePositions.Count)
            {
                warning = $"Колонок в таблице {body.NumberOfColumns}, видимых полей {visiblePositions.Count}: значения ячеек не собраны.";
                return values;
            }

            for (var column = 0; column < body.NumberOfColumns; column++)
            {
                var collected = new HashSet<string>(StringComparer.CurrentCulture);

                for (var row = 0; row < body.NumberOfRows; row++)
                {
                    var text = body.GetCellText(row, column);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        collected.Add(text.Trim());
                    }
                }

                values[visiblePositions[column]] = collected;
            }
        }
        catch (RevitException exception)
        {
            warning = $"Значения ячеек недоступны: {exception.Message}";
            values.Clear();
        }

        return values;
    }

    private ScheduleFieldAudit AuditField(
        ScheduleField field,
        int position,
        IReadOnlyDictionary<int, HashSet<string>> cellValues,
        bool valuesHarvested)
    {
        var distinctValues = cellValues.TryGetValue(position, out var collected)
            ? collected
            : new HashSet<string>(StringComparer.CurrentCulture);

        var reportedValues = distinctValues
            .OrderBy(value => value, StringComparer.CurrentCulture)
            .Take(MaxReportedValuesPerField)
            .ToList();

        var mirroringKind = ResolveMirroringKind(field);
        var combinedParameterNames = new List<string>();
        var combinedParameterLiterals = new List<string>();

        if (mirroringKind is ScheduleMirroringKind.CombinedParameter)
        {
            CollectCombinedParameters(field, combinedParameterNames, combinedParameterLiterals);
        }

        return new ScheduleFieldAudit
        {
            Position = position,
            FieldName = field.GetName(),
            ColumnHeading = field.ColumnHeading,
            FieldType = field.FieldType.ToString(),
            IsHidden = field.IsHidden,
            MirroringKind = mirroringKind,
            RequiresTranslation = RequiresTranslation(mirroringKind, distinctValues, valuesHarvested),
            ParameterName = ResolveParameterName(field.ParameterId),
            IsBuiltInParameter = IsBuiltInParameter(field.ParameterId),
            CombinedParameterNames = combinedParameterNames,
            CombinedParameterLiterals = combinedParameterLiterals,
            DistinctValueCount = distinctValues.Count,
            DistinctValues = reportedValues,
            DistinctValuesTruncated = distinctValues.Count > reportedValues.Count
        };
    }

    private static ScheduleMirroringKind ResolveMirroringKind(ScheduleField field)
    {
        if (field.FieldType is ScheduleFieldType.CombinedParameter)
        {
            return ScheduleMirroringKind.CombinedParameter;
        }

        if (field.FieldType is ScheduleFieldType.Formula)
        {
            return ScheduleMirroringKind.Formula;
        }

        if (IsNumericField(field.FieldType))
        {
            return ScheduleMirroringKind.Untranslated;
        }

        if (IsBuiltInParameter(field.ParameterId))
        {
            return ScheduleMirroringKind.BuiltInParameter;
        }

        return ScheduleMirroringKind.Parameter;
    }

    private static bool IsNumericField(ScheduleFieldType fieldType)
    {
        if (fieldType is ScheduleFieldType.Count or ScheduleFieldType.Percentage)
        {
            return true;
        }

#if REVIT2024_OR_GREATER
        return fieldType is ScheduleFieldType.HostCount;
#else
        return false;
#endif
    }

    private static bool RequiresTranslation(
        ScheduleMirroringKind mirroringKind,
        IReadOnlyCollection<string> distinctValues,
        bool valuesHarvested)
    {
        if (mirroringKind is ScheduleMirroringKind.Untranslated)
        {
            return false;
        }

        if (!valuesHarvested)
        {
            return true;
        }

        return distinctValues.Any(ContainsCyrillic);
    }

    private static bool ContainsCyrillic(string text)
    {
        foreach (var symbol in text)
        {
            if (symbol is >= 'Ѐ' and <= 'ӿ')
            {
                return true;
            }
        }

        return false;
    }

    private void CollectCombinedParameters(ScheduleField field, List<string> names, List<string> literals)
    {
        try
        {
            foreach (var part in field.GetCombinedParameters())
            {
                var name = ResolveParameterName(part.ParamId);
                if (name is not null)
                {
                    names.Add(name);
                }

                AddLiteral(literals, part.Prefix);
                AddLiteral(literals, part.Separator);
                AddLiteral(literals, part.Suffix);
            }
        }
        catch (RevitException)
        {
            names.Clear();
            literals.Clear();
        }
    }

    private static void AddLiteral(List<string> literals, string? literal)
    {
        if (string.IsNullOrWhiteSpace(literal))
        {
            return;
        }

        literals.Add(literal!);
    }

    private string? ResolveParameterName(ElementId parameterId)
    {
        if (parameterId == ElementId.InvalidElementId)
        {
            return null;
        }

        if (IsBuiltInParameter(parameterId))
        {
            var builtInParameter = (BuiltInParameter)(int)GetIdValue(parameterId);

            try
            {
                return LabelUtils.GetLabelFor(builtInParameter);
            }
            catch (RevitException)
            {
                return builtInParameter.ToString();
            }
        }

        return document.GetElement(parameterId)?.Name;
    }

    private static bool IsBuiltInParameter(ElementId parameterId)
    {
        return GetIdValue(parameterId) < 0;
    }

    private static long GetIdValue(ElementId elementId)
    {
#if REVIT2024_OR_GREATER
        return elementId.Value;
#else
        return elementId.IntegerValue;
#endif
    }
}
