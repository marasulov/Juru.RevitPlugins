using System;
using System.Collections.Generic;
using System.Text.Json;

using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

using Juru.RevitPlugins.Schedules.Models;

namespace Juru.RevitPlugins.Schedules.Services;

/// <summary>
///     Хранит русские исходники рядом с элементом, чьи параметры перезаписаны переводом. Расширяемое
///     хранилище выбрано вместо общего параметра: общий параметр требует единого файла на все проекты
///     и расходящихся GUID'ов, а откат нужен в любой модели, где перевод был применён.
/// </summary>
/// <remarks>
///     <para>
///         Исходники лежат словарём «параметр — исходный текст», а не одной строкой: у одного элемента
///         переводится больше одного параметра (у помещения это имя, а дальше будет и отдел), и одно
///         поле означало бы, что второй перевод затирает исходник первого без возможности откатить.
///     </para>
///     <para>
///         Запись ограничена вендором, а чтение открыто: иначе любой другой плагин может затереть
///         сохранённый русский текст, и откат перестанет работать. Vendor id обязателен именно из-за
///         ограниченного уровня доступа — при <c>AccessLevel.Public</c> он не нужен.
///     </para>
/// </remarks>
public static class SourceTextStore
{
    private const string SchemaName = "JuruTranslationSource";
    private const string VendorId = "JURU";
    private const string FieldName = "SourceTexts";

    private static readonly Guid SchemaId = new("6b0d1f42-9c37-4e58-8a21-3d5f7c04b9e6");

    /// <summary>
    ///     Русская строка, сохранённая при записи перевода этого параметра, либо <c>null</c>, если он
    ///     не переводился.
    /// </summary>
    public static string? TryRead(Element element, TextTarget target)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return Read(element).TryGetValue(target.StorageKey, out var stored) && stored.Length > 0
            ? stored
            : null;
    }

    /// <summary>
    ///     Запоминает исходник, если он ещё не запомнен. Повторная запись запрещена намеренно: второй
    ///     прогон перевода иначе сохранил бы английскую строку как исходную и откат стал бы невозможен.
    /// </summary>
    public static void Remember(Element element, TextTarget target, string sourceText)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        var stored = Read(element);
        var key = target.StorageKey;

        if (stored.ContainsKey(key))
        {
            return;
        }

        stored[key] = sourceText;

        Write(element, stored);
    }

    /// <summary>Снимает пометку о переводе параметра: он снова считается непереведённым.</summary>
    public static void Forget(Element element, TextTarget target)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        var stored = Read(element);

        if (!stored.Remove(target.StorageKey))
        {
            return;
        }

        if (stored.Count is 0)
        {
            // Пустую запись оставлять незачем: элемент без исходников ничем не отличается от
            // непереведённого, а лишняя сущность попадает в файл и в журнал предупреждений.
            element.DeleteEntity(ResolveSchema());
            return;
        }

        Write(element, stored);
    }

    private static Dictionary<string, string> Read(Element element)
    {
        var schema = Schema.Lookup(SchemaId);

        if (schema is null)
        {
            return [];
        }

        var entity = element.GetEntity(schema);

        if (!entity.IsValid())
        {
            return [];
        }

        var json = entity.Get<string>(FieldName);

        if (string.IsNullOrEmpty(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        }
        catch (JsonException)
        {
            // Запись оставил кто-то другой или она испорчена: тогда исходников считаем, что нет, —
            // это безопаснее, чем сорвать всю запись перевода.
            return [];
        }
    }

    private static void Write(Element element, Dictionary<string, string> stored)
    {
        var entity = new Entity(ResolveSchema());

        entity.Set(FieldName, JsonSerializer.Serialize(stored));

        element.SetEntity(entity);
    }

    /// <summary>
    ///     Схема живёт в процессе Ревита, а не в документе, поэтому создаётся один раз и переиспользуется
    ///     всеми документами сеанса.
    /// </summary>
    private static Schema ResolveSchema()
    {
        var existing = Schema.Lookup(SchemaId);

        if (existing is not null)
        {
            return existing;
        }

        var builder = new SchemaBuilder(SchemaId);

        builder.SetSchemaName(SchemaName);
        builder.SetVendorId(VendorId);
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Vendor);
        builder.AddSimpleField(FieldName, typeof(string));

        return builder.Finish();
    }
}
