using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Juru.RevitPlugins.SystemViews.Services;

/// <summary>
///     Разбор значения параметра системы и имена, которые плагин даёт видам и фильтрам.
/// </summary>
/// <remarks>
///     <para>
///         Чистая работа со строками, без Revit API: поэтому файл подключается к обычным тестам.
///     </para>
///     <para>
///         В одном значении может стоять несколько систем: оборудование, через которое идут приток
///         и вытяжка, получает «Имя системы» вида «П1,В1». Значение режется по <see cref="Separators" />,
///         и элемент относится к каждой из перечисленных систем.
///     </para>
/// </remarks>
public static class SystemNames
{
    /// <summary>Разделители систем внутри одного значения.</summary>
    public static IReadOnlyList<char> Separators { get; } = [',', ';'];

    /// <summary>Сравнение имён систем. Без учёта регистра - так же сравнивают правила фильтров Ревита.</summary>
    public static StringComparer Comparer { get; } = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    ///     Символы, запрещённые Ревитом в именах видов и фильтров.
    /// </summary>
    private static readonly char[] ForbiddenNameChars = ['\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~'];

    /// <summary>
    ///     Системы, перечисленные в значении параметра: «П1, В1» даёт «П1» и «В1».
    /// </summary>
    public static IReadOnlyList<string> Split(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value!
            .Split(Separators.ToArray())
            .Select(part => part.Trim())
            .Where(part => part.Length > 0)
            .Distinct(Comparer)
            .ToList();
    }

    /// <summary>
    ///     Относится ли значение к системе. Повторяет правила, которые плагин записывает в фильтр вида
    ///     (<see cref="MatchPatterns" />), - так число элементов в окне совпадает с тем, что потом
    ///     останется видно на схеме.
    /// </summary>
    public static bool IsMatch(string? value, string system)
    {
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(system))
        {
            return false;
        }

        return MatchPatterns(system).Any(pattern => pattern.IsMatch(value!));
    }

    /// <summary>
    ///     Условия, по которым значение параметра относится к системе: точное совпадение или
    ///     система в начале, в конце или в середине перечня через разделитель.
    /// </summary>
    /// <remarks>
    ///     Простого «содержит» здесь нет намеренно: «В1» содержится в «В10», и схема В1 показала бы
    ///     чужую систему. Пробел после разделителя допускается («П1, В1»), пробел перед ним - нет.
    /// </remarks>
    public static IReadOnlyList<MatchPattern> MatchPatterns(string system)
    {
        var patterns = new List<MatchPattern> { new(MatchKind.Equals, system) };

        foreach (var separator in Separators)
        {
            patterns.Add(new MatchPattern(MatchKind.BeginsWith, system + separator));
            patterns.Add(new MatchPattern(MatchKind.EndsWith, separator + system));
            patterns.Add(new MatchPattern(MatchKind.EndsWith, separator + " " + system));
            patterns.Add(new MatchPattern(MatchKind.Contains, separator + system + separator));
            patterns.Add(new MatchPattern(MatchKind.Contains, separator + " " + system + separator));
        }

        return patterns;
    }

    /// <summary>Имя вида схемы: префикс и система.</summary>
    public static string ViewName(string prefix, string system)
    {
        return Sanitize(prefix + system);
    }

    /// <summary>
    ///     Имя фильтра, который на схеме скрывает всё, кроме системы. Одно на систему: схемы
    ///     одной системы, сделанные с разных исходных видов, делят один фильтр.
    /// </summary>
    public static string FilterName(string system)
    {
        return Sanitize(FilterPrefix + system);
    }


    /// <summary>
    ///     Шаблон схемы: базовый шаблон плюс фильтр системы. Имя от базового - видно, от чего он
    ///     сделан и что его обновит.
    /// </summary>
    public static string TemplateName(string baseTemplate, string system)
    {
        return Sanitize(baseTemplate + " - " + system);
    }

    /// <summary>Начало имени фильтров плагина: по нему на виде находятся фильтры прошлых запусков.</summary>
    public const string FilterPrefix = "Схема - кроме ";

    /// <summary>Заменяет запрещённые Ревитом символы подчёркиванием.</summary>
    public static string Sanitize(string name)
    {
        var builder = new StringBuilder(name.Trim());

        for (var index = 0; index < builder.Length; index++)
        {
            if (Array.IndexOf(ForbiddenNameChars, builder[index]) >= 0)
            {
                builder[index] = '_';
            }
        }

        return builder.ToString();
    }
}

/// <summary>Вид сравнения строки в правиле фильтра.</summary>
public enum MatchKind
{
    Equals,
    BeginsWith,
    EndsWith,
    Contains
}

/// <summary>Одно условие принадлежности значения к системе.</summary>
public sealed record MatchPattern(MatchKind Kind, string Text)
{
    public bool IsMatch(string value)
    {
        return Kind switch
        {
            MatchKind.Equals => string.Equals(value, Text, StringComparison.OrdinalIgnoreCase),
            MatchKind.BeginsWith => value.StartsWith(Text, StringComparison.OrdinalIgnoreCase),
            MatchKind.EndsWith => value.EndsWith(Text, StringComparison.OrdinalIgnoreCase),
            MatchKind.Contains => value.IndexOf(Text, StringComparison.OrdinalIgnoreCase) >= 0,
            _ => false
        };
    }
}
