using System;
using System.Linq;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Schedules.Models;

namespace Juru.RevitPlugins.Schedules.Services;

/// <summary>
///     Читает и пишет текст по цели. Всё знание о том, как устроена каждая цель, собрано здесь: иначе
///     оно расползётся по сборщикам категорий, и правило чтения разойдётся с правилом записи.
/// </summary>
public static class TextTargetAccess
{
    /// <summary>Что сейчас написано в цели. Пусто, если цели у элемента нет.</summary>
    public static string Read(Element element, TextTarget target)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        return target.Kind switch
        {
            TextTargetKind.Parameter => FindParameter(element, target)?.AsString() ?? string.Empty,
            TextTargetKind.Name => element.Name ?? string.Empty,
            TextTargetKind.FamilyName => (element as ElementType)?.FamilyName ?? string.Empty,
            TextTargetKind.NoteText => (element as TextElement)?.Text ?? string.Empty,
            TextTargetKind.ScheduleColumn => FindField(element, target)?.ColumnHeading ?? string.Empty,
            TextTargetKind.ScheduleFilter => ReadFilter(element, target),
            TextTargetKind.DimensionText => ReadDimension(element, target),
            TextTargetKind.RevisionField => ReadRevision(element, target),
            TextTargetKind.GlobalParameterValue => ReadGlobalParameter(element),
            _ => string.Empty
        };
    }

    /// <summary>
    ///     Записывает текст в цель. Ложь означает, что цели у элемента больше нет — например колонка
    ///     спецификации удалена после того, как окно прочитало модель.
    /// </summary>
    public static bool Write(Element element, TextTarget target, string text)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        switch (target.Kind)
        {
            case TextTargetKind.Parameter:
                return FindParameter(element, target)?.Set(text) ?? false;

            case TextTargetKind.Name:
                // Имена видов, уровней и типоразмеров уникальны в документе: столкновение Ревит
                // отклоняет исключением, и строка попадёт в отчёт как неудача.
                element.Name = text;
                return true;

            case TextTargetKind.NoteText:
                if (element is not TextElement note)
                {
                    return false;
                }

                note.Text = text;
                return true;

            case TextTargetKind.ScheduleColumn:
                var field = FindField(element, target);

                if (field is null)
                {
                    return false;
                }

                field.ColumnHeading = text;
                return true;

            case TextTargetKind.ScheduleFilter:
                return WriteFilter(element, target, text);

            case TextTargetKind.DimensionText:
                return WriteDimension(element, target, text);

            case TextTargetKind.RevisionField:
                return WriteRevision(element, target, text);

            case TextTargetKind.GlobalParameterValue:
                if (element is not GlobalParameter global)
                {
                    return false;
                }

                global.SetValue(new StringParameterValue(text));
                return true;

            // Имя семейства меняется только у самого семейства, а не у типоразмера в проекте:
            // редактировать его пришлось бы в редакторе семейств, чего плагин не делает.
            // Ключевые примечания записываются перезаписью внешнего файла, а не здесь.
            case TextTargetKind.FamilyName:
            case TextTargetKind.KeynoteEntry:
            default:
                return false;
        }
    }

    private static string ReadGlobalParameter(Element element)
    {
        if (element is not GlobalParameter global)
        {
            return string.Empty;
        }

        return global.GetValue() is StringParameterValue value ? value.Value ?? string.Empty : string.Empty;
    }

    private static Parameter? FindParameter(Element element, TextTarget target)
    {
        if (Enum.TryParse<BuiltInParameter>(target.Key, out var builtIn))
        {
            var parameter = element.get_Parameter(builtIn);

            if (parameter is not null)
            {
                return parameter.IsReadOnly ? null : parameter;
            }
        }

        // Общий или проектный параметр: у него нет встроенного номера, только имя.
        var byName = element.LookupParameter(target.Caption);

        return byName is null || byName.IsReadOnly ? null : byName;
    }

    private static ScheduleField? FindField(Element element, TextTarget target)
    {
        if (element is not ViewSchedule schedule)
        {
            return null;
        }

        var definition = schedule.Definition;

        return Enumerable
            .Range(0, definition.GetFieldCount())
            .Select(definition.GetField)
            .FirstOrDefault(field => string.Equals(field.GetName(), target.Key, StringComparison.Ordinal));
    }

    private static ScheduleFilter? FindFilter(Element element, TextTarget target, out ScheduleDefinition? definition)
    {
        definition = (element as ViewSchedule)?.Definition;

        if (definition is null || !int.TryParse(target.Key, out var index))
        {
            return null;
        }

        return index >= 0 && index < definition.GetFilterCount() ? definition.GetFilter(index) : null;
    }

    private static string ReadFilter(Element element, TextTarget target)
    {
        var filter = FindFilter(element, target, out _);

        return filter is { IsStringValue: true } ? filter.GetStringValue() : string.Empty;
    }

    private static bool WriteFilter(Element element, TextTarget target, string text)
    {
        var filter = FindFilter(element, target, out var definition);

        if (filter is not { IsStringValue: true } || definition is null || !int.TryParse(target.Key, out var index))
        {
            return false;
        }

        filter.SetValue(text);
        definition.SetFilter(index, filter);

        return true;
    }

    private static string ReadDimension(Element element, TextTarget target)
    {
        if (element is not Dimension dimension || !target.TryReadDimensionKey(out var place, out var segment))
        {
            return string.Empty;
        }

        if (segment is null)
        {
            return ReadPlace(dimension.Above, dimension.Below, dimension.Prefix, dimension.Suffix, dimension.ValueOverride, place);
        }

        var found = FindSegment(dimension, segment.Value);

        return found is null
            ? string.Empty
            : ReadPlace(found.Above, found.Below, found.Prefix, found.Suffix, found.ValueOverride, place);
    }

    private static bool WriteDimension(Element element, TextTarget target, string text)
    {
        if (element is not Dimension dimension || !target.TryReadDimensionKey(out var place, out var segment))
        {
            return false;
        }

        if (segment is null)
        {
            switch (place)
            {
                case DimensionTextPlace.Above: dimension.Above = text; return true;
                case DimensionTextPlace.Below: dimension.Below = text; return true;
                case DimensionTextPlace.Prefix: dimension.Prefix = text; return true;
                case DimensionTextPlace.Suffix: dimension.Suffix = text; return true;
                case DimensionTextPlace.ValueOverride: dimension.ValueOverride = text; return true;
                default: return false;
            }
        }

        var found = FindSegment(dimension, segment.Value);

        if (found is null)
        {
            return false;
        }

        switch (place)
        {
            case DimensionTextPlace.Above: found.Above = text; return true;
            case DimensionTextPlace.Below: found.Below = text; return true;
            case DimensionTextPlace.Prefix: found.Prefix = text; return true;
            case DimensionTextPlace.Suffix: found.Suffix = text; return true;
            case DimensionTextPlace.ValueOverride: found.ValueOverride = text; return true;
            default: return false;
        }
    }

    private static DimensionSegment? FindSegment(Dimension dimension, int index)
    {
        var segments = dimension.Segments;

        if (segments is null || index < 0 || index >= segments.Size)
        {
            return null;
        }

        return segments.Cast<DimensionSegment>().ElementAtOrDefault(index);
    }

    private static string ReadPlace(
        string? above,
        string? below,
        string? prefix,
        string? suffix,
        string? valueOverride,
        DimensionTextPlace place)
    {
        return place switch
        {
            DimensionTextPlace.Above => above ?? string.Empty,
            DimensionTextPlace.Below => below ?? string.Empty,
            DimensionTextPlace.Prefix => prefix ?? string.Empty,
            DimensionTextPlace.Suffix => suffix ?? string.Empty,
            DimensionTextPlace.ValueOverride => valueOverride ?? string.Empty,
            _ => string.Empty
        };
    }

    private static string ReadRevision(Element element, TextTarget target)
    {
        if (element is not Revision revision || !Enum.TryParse<RevisionTextField>(target.Key, out var field))
        {
            return string.Empty;
        }

        return field switch
        {
            RevisionTextField.Description => revision.Description ?? string.Empty,
            RevisionTextField.IssuedTo => revision.IssuedTo ?? string.Empty,
            RevisionTextField.IssuedBy => revision.IssuedBy ?? string.Empty,
            _ => string.Empty
        };
    }

    private static bool WriteRevision(Element element, TextTarget target, string text)
    {
        if (element is not Revision revision || !Enum.TryParse<RevisionTextField>(target.Key, out var field))
        {
            return false;
        }

        switch (field)
        {
            case RevisionTextField.Description: revision.Description = text; return true;
            case RevisionTextField.IssuedTo: revision.IssuedTo = text; return true;
            case RevisionTextField.IssuedBy: revision.IssuedBy = text; return true;
            default: return false;
        }
    }
}
