using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace Juru.RevitPlugins.Schedules.Services;

/// <summary>
///     Связь копии спецификации с оригиналом: копия помнит, с чего снята.
/// </summary>
/// <remarks>
///     <para>
///         Без связи копию находили бы по имени, а имя - вещь ненадёжная: оригинал переименовали, и
///         следующий прогон делает вторую копию рядом с прежней, вместо того чтобы обновить её. Связь
///         держится на <c>UniqueId</c> оригинала: он переживает и переименование, и синхронизацию.
///     </para>
///     <para>
///         Хранится в самой копии расширяемым хранилищем - по той же причине, что и русские исходники
///         в <see cref="SourceTextStore" />: общий параметр потребовал бы файла общих параметров на все
///         проекты, а связь нужна в любой модели.
///     </para>
/// </remarks>
public static class ScheduleCopyLink
{
    private const string SchemaName = "JuruScheduleCopySource";
    private const string VendorId = "JURU";
    private const string FieldName = "SourceUniqueId";

    private static readonly Guid SchemaId = new("1f4a0c37-5e62-4c8d-9d41-2a7b6e08c5d3");

    /// <summary>Запоминает оригинал, с которого снята копия.</summary>
    public static void Remember(View copy, Element original)
    {
        if (copy is null)
        {
            throw new ArgumentNullException(nameof(copy));
        }

        if (original is null)
        {
            throw new ArgumentNullException(nameof(original));
        }

        var entity = new Entity(ResolveSchema());

        entity.Set(FieldName, original.UniqueId);

        copy.SetEntity(entity);
    }

    /// <summary>Оригинал этой копии либо <c>null</c>, если вид копией не является.</summary>
    public static string? TryReadOriginalId(Element copy)
    {
        if (copy is null)
        {
            throw new ArgumentNullException(nameof(copy));
        }

        if (Schema.Lookup(SchemaId) is not { } schema)
        {
            return null;
        }

        var entity = copy.GetEntity(schema);

        if (!entity.IsValid())
        {
            return null;
        }

        var stored = entity.Get<string>(FieldName);

        return string.IsNullOrEmpty(stored) ? null : stored;
    }

    /// <summary>
    ///     Оригинал, с которого снята копия, либо <c>null</c>: вид копией не является или оригинал удалён.
    /// </summary>
    public static ViewSchedule? TryFindOriginal(Document document, Element copy)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (TryReadOriginalId(copy) is not { } uniqueId)
        {
            return null;
        }

        return document.GetElement(uniqueId) as ViewSchedule;
    }

    /// <summary>
    ///     Прежние копии этого оригинала. Их несколько, если прошлый прогон оборвался на полпути.
    /// </summary>
    public static IReadOnlyList<ViewSchedule> FindCopies(Document document, Element original)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (original is null)
        {
            throw new ArgumentNullException(nameof(original));
        }

        var uniqueId = original.UniqueId;

        return new FilteredElementCollector(document)
            .OfClass(typeof(ViewSchedule))
            .Cast<ViewSchedule>()
            .Where(view => string.Equals(TryReadOriginalId(view), uniqueId, StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>
    ///     Все переведённые копии модели: виды, помнящие свой оригинал.
    /// </summary>
    public static IReadOnlyList<ViewSchedule> FindAllCopies(Document document)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        return new FilteredElementCollector(document)
            .OfClass(typeof(ViewSchedule))
            .Cast<ViewSchedule>()
            .Where(view => TryReadOriginalId(view) is not null)
            .ToList();
    }

    /// <summary>
    ///     Схема живёт в процессе Ревита, а не в документе, поэтому создаётся один раз и переиспользуется
    ///     всеми документами сеанса. Запись открыта: Ревит сверяет вендора того add-in, под которым идёт
    ///     команда, а он у нас разный - см. <see cref="SourceTextStore" />.
    /// </summary>
    private static Schema ResolveSchema()
    {
        if (Schema.Lookup(SchemaId) is { } existing)
        {
            return existing;
        }

        var builder = new SchemaBuilder(SchemaId);

        builder.SetSchemaName(SchemaName);
        builder.SetVendorId(VendorId);
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Public);
        builder.AddSimpleField(FieldName, typeof(string));

        return builder.Finish();
    }
}
