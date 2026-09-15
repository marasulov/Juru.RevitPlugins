using System.Collections.Generic;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Schedules.Models;

/// <summary>
///     Где искать переводимый текст: во всей модели или только в активном виде.
/// </summary>
/// <param name="ViewId">
///     Активный вид или <c>null</c> для всей модели. Категории, у которых вида нет вовсе — листы,
///     уровни, материалы, ревизии, ключевые примечания, — его игнорируют: они не «в виде» и от
///     области сканирования не зависят.
/// </param>

namespace Juru.RevitPlugins.Schedules.Services.Categories;

/// <summary>
///     Категория модели, чьи тексты переводятся записью в параметр. Категория умеет только собирать свои
///     строки: записью и откатом занимается <see cref="ElementTextWriter" />, одинаковый для всех
///     категорий, — иначе правило хранения исходника расползётся по реализациям.
/// </summary>
public interface ITranslatableCategory
{
    /// <summary>
    ///     Устойчивое имя категории для кода: разметка ищет вкладку по нему, потому что
    ///     <see cref="Name" /> переводится и меняется вместе с языком интерфейса.
    /// </summary>
    string Key { get; }

    /// <summary>Имя вкладки в окне.</summary>
    string Name { get; }

    /// <summary>Что показать вместо таблицы, если такого текста в модели нет.</summary>
    string EmptyNote { get; }

    /// <summary>
    ///     Строки этой категории, требующие перевода: русские, а также уже переведённые — их видно
    ///     в списке, чтобы перевод можно было поправить или откатить.
    /// </summary>
    IReadOnlyList<ElementText> Collect(CollectScope scope);
}

public sealed record CollectScope(Document Document, ElementId? ViewId)
{
    /// <summary>Экземпляры категории в области сканирования.</summary>
    public FilteredElementCollector Instances(BuiltInCategory category)
    {
        return Collector().OfCategory(category).WhereElementIsNotElementType();
    }

    /// <summary>Экземпляры класса в области сканирования.</summary>
    public FilteredElementCollector Instances(System.Type type)
    {
        return Collector().OfClass(type).WhereElementIsNotElementType();
    }

    /// <summary>Всё по классу, включая типы: у видов и листов экземпляров как таковых нет.</summary>
    public FilteredElementCollector OfClass(System.Type type)
    {
        return Collector().OfClass(type);
    }

    /// <summary>Вся модель, независимо от области: для категорий, которые вида не имеют.</summary>
    public FilteredElementCollector WholeModel()
    {
        return new FilteredElementCollector(Document);
    }

    private FilteredElementCollector Collector()
    {
        return ViewId is null
            ? new FilteredElementCollector(Document)
            : new FilteredElementCollector(Document, ViewId);
    }
}
