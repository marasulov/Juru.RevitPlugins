using System;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Schedules.Localization;

namespace Juru.RevitPlugins.Schedules.Models;

/// <summary>
///     Куда именно пишется перевод. Далеко не всякий переводимый текст лежит в параметре: имя вида,
///     текст примечания, заголовок колонки спецификации и надпись у размера — это свойства объекта,
///     а не параметры, и записываются каждое по-своему.
/// </summary>
public enum TextTargetKind
{
    /// <summary>Параметр элемента: <see cref="TextTarget.Key" /> — имя <see cref="BuiltInParameter" />.</summary>
    Parameter,

    /// <summary>Имя элемента: уровень, материал, вид, лист, типоразмер, спецификация.</summary>
    Name,

    /// <summary>Имя семейства у типоразмера.</summary>
    FamilyName,

    /// <summary>Текст текстового примечания.</summary>
    NoteText,

    /// <summary>Заголовок колонки спецификации: <see cref="TextTarget.Key" /> — имя поля.</summary>
    ScheduleColumn,

    /// <summary>Строковое значение фильтра спецификации: <see cref="TextTarget.Key" /> — его номер.</summary>
    ScheduleFilter,

    /// <summary>Надпись у размера: <see cref="TextTarget.Key" /> — «место[:номер сегмента]».</summary>
    DimensionText,

    /// <summary>Поле ревизии: <see cref="TextTarget.Key" /> — имя поля.</summary>
    RevisionField,

    /// <summary>Значение глобального параметра типа «Текст».</summary>
    GlobalParameterValue,

    /// <summary>
    ///     Описание в таблице ключевых примечаний: <see cref="TextTarget.Key" /> — ключ примечания.
    /// </summary>
    /// <remarks>
    ///     Живёт во внешнем файле, а не в элементе, поэтому записывается не через
    ///     <c>TextTargetAccess</c>, а перезаписью файла целиком.
    /// </remarks>
    KeynoteEntry
}

/// <summary>
///     Одна цель записи перевода.
/// </summary>
public sealed record TextTarget
{
    private TextTarget(TextTargetKind kind, string key, string caption)
    {
        Kind = kind;
        Key = key;
        Caption = caption;
    }

    public TextTargetKind Kind { get; }

    /// <summary>Уточнение внутри вида цели: имя параметра, имя поля, номер сегмента.</summary>
    public string Key { get; }

    /// <summary>Как назвать цель пользователю: «Имя», «Отдел», «Заголовок: Марка».</summary>
    public string Caption { get; }

    /// <summary>
    ///     Ключ, под которым исходник лежит в элементе. Устойчив к переименованиям в коде: строится
    ///     из вида цели и уточнения, а не из порядкового номера.
    /// </summary>
    public string StorageKey
    {
        get { return Key.Length is 0 ? Kind.ToString() : $"{Kind}:{Key}"; }
    }

    public static TextTarget Parameter(BuiltInParameter parameter, string caption)
    {
        return new TextTarget(TextTargetKind.Parameter, parameter.ToString(), caption);
    }

    /// <summary>
    ///     Общий или проектный параметр по имени. Ключ отличается от имени встроенного параметра
    ///     Ревита символом впереди — иначе одноимённый общий параметр (случай маловероятный, но
    ///     возможный) читался бы как встроенный.
    /// </summary>
    public static TextTarget Parameter(string parameterName)
    {
        return new TextTarget(TextTargetKind.Parameter, $"#{parameterName}", parameterName);
    }

    public static TextTarget Name(string? caption = null)
    {
        return new TextTarget(TextTargetKind.Name, string.Empty, caption ?? Text.TargetName);
    }

    public static TextTarget FamilyName()
    {
        return new TextTarget(TextTargetKind.FamilyName, string.Empty, Text.TargetFamilyName);
    }

    public static TextTarget NoteText()
    {
        return new TextTarget(TextTargetKind.NoteText, string.Empty, Text.TargetNoteText);
    }

    public static TextTarget ScheduleColumn(string fieldName)
    {
        return new TextTarget(
            TextTargetKind.ScheduleColumn,
            fieldName,
            $"{Text.TargetHeadingPrefix}: {fieldName}");
    }

    public static TextTarget ScheduleFilter(int index, string fieldName)
    {
        return new TextTarget(
            TextTargetKind.ScheduleFilter,
            index.ToString(),
            $"{Text.TargetFilterPrefix} {index + 1}: {fieldName}");
    }

    public static TextTarget DimensionText(DimensionTextPlace place, int? segment)
    {
        var key = segment is null ? place.ToString() : $"{place}:{segment}";
        var caption = segment is null
            ? Describe(place)
            : $"{Describe(place)}, {Text.TargetSegment} {segment + 1}";

        return new TextTarget(TextTargetKind.DimensionText, key, caption);
    }

    public static TextTarget RevisionField(RevisionTextField field)
    {
        return new TextTarget(TextTargetKind.RevisionField, field.ToString(), Describe(field));
    }

    public static TextTarget GlobalParameterValue(string parameterName)
    {
        return new TextTarget(TextTargetKind.GlobalParameterValue, string.Empty, parameterName);
    }

    public static TextTarget KeynoteEntry(string key)
    {
        return new TextTarget(TextTargetKind.KeynoteEntry, key, Text.TargetDescription);
    }

    /// <summary>Разбирает <see cref="Key" /> надписи размера обратно в место и номер сегмента.</summary>
    public bool TryReadDimensionKey(out DimensionTextPlace place, out int? segment)
    {
        var parts = Key.Split(':');

        segment = parts.Length > 1 && int.TryParse(parts[1], out var index) ? index : null;

        return Enum.TryParse(parts[0], out place);
    }

    private static string Describe(DimensionTextPlace place)
    {
        return place switch
        {
            DimensionTextPlace.Above => Text.TargetAbove,
            DimensionTextPlace.Below => Text.TargetBelow,
            DimensionTextPlace.Prefix => Text.TargetPrefix,
            DimensionTextPlace.Suffix => Text.TargetSuffix,
            DimensionTextPlace.ValueOverride => Text.TargetValueOverride,
            _ => place.ToString()
        };
    }

    private static string Describe(RevisionTextField field)
    {
        return field switch
        {
            RevisionTextField.Description => Text.TargetDescription,
            RevisionTextField.IssuedTo => Text.TargetIssuedTo,
            RevisionTextField.IssuedBy => Text.TargetIssuedBy,
            _ => field.ToString()
        };
    }
}

/// <summary>Место надписи у размера.</summary>
public enum DimensionTextPlace
{
    Above,
    Below,
    Prefix,
    Suffix,
    ValueOverride
}

/// <summary>Переводимое поле ревизии.</summary>
public enum RevisionTextField
{
    Description,
    IssuedTo,
    IssuedBy
}
