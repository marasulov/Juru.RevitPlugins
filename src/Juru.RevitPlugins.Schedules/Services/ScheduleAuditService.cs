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
    private const int MaxSampledElementsPerSchedule = 2000;
    private const string MultiCategoryName = "<несколько категорий>";

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

        var elements = CollectScheduledElements(schedule, out var warning);
        var fieldValues = HarvestFieldValues(fields, elements);
        HarvestOverriddenCells(schedule, fields, fieldValues);

        var fieldAudits = fields
            .Select((field, position) => AuditField(field, position, fieldValues))
            .ToList();

        sheetPlacements.TryGetValue(schedule.Id, out var placementCount);

        return new ScheduleAudit
        {
            ScheduleName = schedule.Name,
            CategoryName = ResolveCategoryName(definition),
            IsKeySchedule = definition.IsKeySchedule,
            SheetPlacementCount = placementCount,
            ElementCount = elements.Count,
            Fields = fieldAudits,
            SortGroupFieldNames = ResolveSortGroupFieldNames(definition),
            Filters = AuditFilters(definition),
            ValueHarvestWarning = warning
        };
    }

    /// <summary>
    ///     Возвращает элементы, попадающие в спецификацию. Значения читаются с них, а не из таблицы вида:
    ///     тело спецификации не пересчитывается, пока вид закрыт, и отдаёт устаревшие строки.
    /// </summary>
    private IReadOnlyList<Element> CollectScheduledElements(ViewSchedule schedule, out string? warning)
    {
        warning = null;

        try
        {
            var scheduled = new FilteredElementCollector(document, schedule.Id)
                .WhereElementIsNotElementType()
                .Take(MaxSampledElementsPerSchedule)
                .ToList();

            if (scheduled.Count > 0)
            {
                return scheduled;
            }

            var byCategory = CollectByCategory(schedule.Definition.CategoryId);

            if (byCategory.Count > 0)
            {
                warning = "Вид не отдал элементы: значения собраны по всей категории, без фильтров спецификации.";
                return byCategory;
            }

            warning = "Элементы не найдены ни по виду, ни по категории.";
            return [];
        }
        catch (RevitException exception)
        {
            warning = $"Элементы спецификации недоступны: {exception.Message}";
            return [];
        }
    }

    private IReadOnlyList<Element> CollectByCategory(ElementId categoryId)
    {
        if (categoryId == ElementId.InvalidElementId)
        {
            return [];
        }

        return new FilteredElementCollector(document)
            .OfCategoryId(categoryId)
            .WhereElementIsNotElementType()
            .Take(MaxSampledElementsPerSchedule)
            .ToList();
    }

    private Dictionary<int, HashSet<string>> HarvestFieldValues(
        IReadOnlyList<ScheduleField> fields,
        IReadOnlyList<Element> elements)
    {
        var values = new Dictionary<int, HashSet<string>>();

        var sources = new List<FieldParameterSource>();
        for (var position = 0; position < fields.Count; position++)
        {
            sources.AddRange(ResolveParameterSources(fields[position], position));
        }

        if (sources.Count is 0)
        {
            return values;
        }

        foreach (var element in elements)
        {
            var elementType = document.GetElement(element.GetTypeId());

            foreach (var source in sources)
            {
                var text = ReadParameterText(element, elementType, source);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                if (!values.TryGetValue(source.Position, out var collected))
                {
                    collected = new HashSet<string>(StringComparer.CurrentCulture);
                    values[source.Position] = collected;
                }

                collected.Add(text!.Trim());
            }
        }

        return values;
    }

    /// <summary>
    ///     Добавляет текст ячеек, вписанный поверх данных вручную. Он не хранится ни в одном параметре,
    ///     а спецификации-таблицы вроде ведомости ссылочных документов состоят из него целиком.
    /// </summary>
    private static void HarvestOverriddenCells(
        ViewSchedule schedule,
        IReadOnlyList<ScheduleField> fields,
        Dictionary<int, HashSet<string>> values)
    {
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
                return;
            }

            for (var column = 0; column < body.NumberOfColumns; column++)
            {
                for (var row = 0; row < body.NumberOfRows; row++)
                {
                    if (!body.IsCellOverridden(row, column))
                    {
                        continue;
                    }

                    var text = body.GetCellText(row, column);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    var position = visiblePositions[column];

                    if (!values.TryGetValue(position, out var collected))
                    {
                        collected = new HashSet<string>(StringComparer.CurrentCulture);
                        values[position] = collected;
                    }

                    collected.Add(text.Trim());
                }
            }
        }
        catch (RevitException)
        {
            // Таблица недоступна: значения параметров уже собраны, переопределений не будет.
        }
    }

    /// <summary>
    ///     Раскладывает колонку на параметры, значения которых можно прочитать с элемента. Комбинированная
    ///     колонка даёт несколько параметров, вычисляемое значение — ни одного.
    /// </summary>
    private IEnumerable<FieldParameterSource> ResolveParameterSources(ScheduleField field, int position)
    {
        if (field.FieldType is ScheduleFieldType.CombinedParameter)
        {
            foreach (var part in ReadCombinedParameters(field))
            {
                var source = CreateParameterSource(part.ParamId, position);
                if (source is not null)
                {
                    yield return source;
                }
            }

            yield break;
        }

        if (field.FieldType is ScheduleFieldType.Formula)
        {
            yield break;
        }

        if (IsNumericField(field.FieldType))
        {
            yield break;
        }

        var fieldSource = CreateParameterSource(field.ParameterId, position);
        if (fieldSource is not null)
        {
            yield return fieldSource;
        }
    }

    private FieldParameterSource? CreateParameterSource(ElementId parameterId, int position)
    {
        if (parameterId == ElementId.InvalidElementId)
        {
            return null;
        }

        if (IsBuiltInParameter(parameterId))
        {
            return new FieldParameterSource
            {
                Position = position,
                BuiltInParameter = (BuiltInParameter)(int)GetIdValue(parameterId)
            };
        }

        if (document.GetElement(parameterId) is not ParameterElement parameterElement)
        {
            return null;
        }

        return new FieldParameterSource
        {
            Position = position,
            Definition = parameterElement.GetDefinition()
        };
    }

    /// <summary>
    ///     Читает значение параметра с экземпляра, а при его отсутствии — с типа. Числа пропускаются:
    ///     переводу подлежат только строки и ссылки на именованные элементы.
    /// </summary>
    private static string? ReadParameterText(Element element, Element? elementType, FieldParameterSource source)
    {
        var parameter = FindParameter(element, source) ?? FindParameter(elementType, source);

        if (parameter is null)
        {
            return null;
        }

        return parameter.StorageType switch
        {
            StorageType.String => parameter.AsString(),
            StorageType.ElementId => parameter.AsValueString(),
            _ => null
        };
    }

    private static Parameter? FindParameter(Element? element, FieldParameterSource source)
    {
        if (element is null)
        {
            return null;
        }

        if (source.BuiltInParameter is { } builtInParameter)
        {
            return element.get_Parameter(builtInParameter);
        }

        if (source.Definition is null)
        {
            return null;
        }

        return element.get_Parameter(source.Definition);
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

    private ScheduleFieldAudit AuditField(
        ScheduleField field,
        int position,
        IReadOnlyDictionary<int, HashSet<string>> fieldValues)
    {
        var distinctValues = fieldValues.TryGetValue(position, out var collected)
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
            RequiresTranslation = RequiresTranslation(mirroringKind, distinctValues),
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

    /// <summary>
    ///     Вычисляемое значение помечается всегда: его формулу API не отдаёт, и колонку в любом случае
    ///     разбирает человек.
    /// </summary>
    private static bool RequiresTranslation(ScheduleMirroringKind mirroringKind, IReadOnlyCollection<string> distinctValues)
    {
        if (mirroringKind is ScheduleMirroringKind.Untranslated)
        {
            return false;
        }

        if (mirroringKind is ScheduleMirroringKind.Formula)
        {
            return true;
        }

        return distinctValues.Any(RussianText.ContainsCyrillic);
    }

    private void CollectCombinedParameters(ScheduleField field, List<string> names, List<string> literals)
    {
        foreach (var part in ReadCombinedParameters(field))
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

    private static IReadOnlyList<TableCellCombinedParameterData> ReadCombinedParameters(ScheduleField field)
    {
        try
        {
            return field.GetCombinedParameters().ToList();
        }
        catch (RevitException)
        {
            return [];
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

    private sealed record FieldParameterSource
    {
        public required int Position { get; init; }
        public BuiltInParameter? BuiltInParameter { get; init; }
        public Definition? Definition { get; init; }
    }
}
