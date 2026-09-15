using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Schedules.Models;

using Juru.RevitPlugins.Schedules.Localization;

namespace Juru.RevitPlugins.Schedules.Services.Categories;

/// <summary>
///     Собственный текст спецификации: имя, заголовки видимых колонок и строковые значения фильтров.
/// </summary>
/// <remarks>
///     <para>
///         Это всё, что в спецификации доступно API на запись. Значения ячеек не переопределяются:
///         <c>TableSectionData.SetCellText</c> для тела обычной спецификации запрещён. Поэтому такой
///         перевод даёт живую спецификацию с английской шапкой, а значения в ней станут английскими
///         только по мере перевода самих объектов модели — помещений, материалов, типоразмеров.
///     </para>
///     <para>
///         Фильтры переводятся обязательно вместе с колонками: фильтр по строковому значению после
///         перевода параметра перестаёт совпадать, и спецификация оказывается пустой.
///     </para>
/// </remarks>
public sealed class ScheduleTexts : TranslatableCategory
{
    public override string Key
    {
        get { return "ScheduleTexts"; }
    }

    public override string Name
    {
        get { return Text.CategoryScheduleTexts; }
    }

    public override string EmptyNote
    {
        get { return Text.EmptyScheduleTexts; }
    }

    public override IReadOnlyList<ElementText> Collect(CollectScope scope)
    {
        var schedules = scope.WholeModel()
            .OfClass(typeof(ViewSchedule))
            .Cast<ViewSchedule>()
            .Where(schedule => !schedule.IsTitleblockRevisionSchedule && !schedule.IsInternalKeynoteSchedule);

        return Collect(schedules);
    }

    /// <summary>Собирает текст только выбранных спецификаций: перевод на месте идёт по отмеченному.</summary>
    public IReadOnlyList<ElementText> Collect(IEnumerable<ViewSchedule> schedules)
    {
        return Order(schedules.SelectMany(Collect));
    }

    private IEnumerable<ElementText> Collect(ViewSchedule schedule)
    {
        var definition = schedule.Definition;
        var context = schedule.Name;

        var targets = new List<TextTarget> { TextTarget.Name(Text.TargetScheduleName) };

        foreach (var index in Enumerable.Range(0, definition.GetFieldCount()))
        {
            var field = definition.GetField(index);

            if (!field.IsHidden)
            {
                targets.Add(TextTarget.ScheduleColumn(field.GetName()));
            }
        }

        foreach (var index in Enumerable.Range(0, definition.GetFilterCount()))
        {
            var filter = definition.GetFilter(index);

            if (!filter.IsStringValue)
            {
                continue;
            }

            var fieldName = TryGetFieldName(definition, filter);

            targets.Add(TextTarget.ScheduleFilter(index, fieldName));
        }

        return DescribeAll(schedule, context, targets.ToArray());
    }

    /// <summary>
    ///     Имя поля фильтра. Поле могли скрыть или удалить — тогда остаётся только номер фильтра.
    /// </summary>
    private static string TryGetFieldName(ScheduleDefinition definition, ScheduleFilter filter)
    {
        try
        {
            return definition.GetField(filter.FieldId)?.GetName() ?? string.Empty;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return string.Empty;
        }
    }
}
