using System;
using System.Linq;
using System.Text;

namespace Juru.RevitPlugins.Schedules.Services;

/// <summary>
///     Разбор русского текста спецификаций: что подлежит переводу и как показать это переводчику.
/// </summary>
public static class RussianText
{
    public static bool ContainsCyrillic(string? text)
    {
        if (text is null)
        {
            return false;
        }

        foreach (var symbol in text)
        {
            if (symbol is >= 'Ѐ' and <= 'ӿ')
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Убирает ручные переносы и двойные пробелы, которыми заголовок подгоняют под узкую колонку:
    ///     «Кат. поме-  ще-  ния» превращается в «Кат. помещения». Переносы строк сохраняются.
    /// </summary>
    public static string Normalize(string text)
    {
        var normalized = new StringBuilder(text.Length);

        for (var index = 0; index < text.Length; index++)
        {
            var symbol = text[index];

            if (symbol is '-' && SkipsWhitespaceInsideWord(text, index))
            {
                index = SkipWhitespace(text, index + 1) - 1;
                continue;
            }

            if (symbol is ' ' or '\t')
            {
                if (normalized.Length is 0 || normalized[^1] is ' ' or '\n')
                {
                    continue;
                }

                normalized.Append(' ');
                continue;
            }

            normalized.Append(symbol);
        }

        return normalized.ToString().Trim();
    }

    /// <summary>
    ///     Отделяет английскую половину уже двуязычной строки: «Sheet number / Номер листа» даёт
    ///     «Sheet number». Пусто, если такой половины нет.
    /// </summary>
    public static string TakeEnglishPart(string text)
    {
        var parts = text.Split('/');

        if (parts.Length < 2)
        {
            return string.Empty;
        }

        var english = parts
            .Select(part => part.Trim())
            .Where(part => part.Length > 0 && !ContainsCyrillic(part))
            .ToList();

        return english.Count is 0 ? string.Empty : string.Join(" / ", english);
    }

    /// <summary>
    ///     Обозначения стандартов, которые в переводе остаются собой: «ГОСТ 530-2012», «КМК 2.01.01-94».
    /// </summary>
    private static readonly string[] StandardPrefixes = ["ГОСТ", "ТУ", "КМК", "ШНК", "СНиП", "СН", "СП"];

    /// <summary>
    ///     Распознаёт обозначение стандарта: все слова либо из известного набора аббревиатур, либо вовсе без
    ///     кириллицы. «Серия 1.031.9-2.07 вып. 4» под правило не попадает — там есть слова для перевода.
    /// </summary>
    public static bool IsStandardDesignation(string text)
    {
        var words = text.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

        if (words.Length < 2)
        {
            return false;
        }

        var hasPrefix = false;

        foreach (var word in words)
        {
            if (StandardPrefixes.Contains(word, StringComparer.CurrentCulture))
            {
                hasPrefix = true;
                continue;
            }

            if (ContainsCyrillic(word))
            {
                return false;
            }
        }

        return hasPrefix;
    }

    /// <summary>
    ///     Распознаёт марку изделия: одно слово с цифрой, вроде «ДВ-2», «КП-120», «ФЭ-П-1». Марки в переводе
    ///     сохраняются, чтобы совпадать с планами и разрезами.
    /// </summary>
    public static bool IsProductMark(string text)
    {
        var mark = text.Trim();

        if (mark.Length is 0 or > 12 || !ContainsCyrillic(mark))
        {
            return false;
        }

        var hasDigit = false;

        foreach (var symbol in mark)
        {
            if (char.IsDigit(symbol))
            {
                hasDigit = true;
                continue;
            }

            if (!char.IsLetter(symbol) && symbol is not ('-' or '.'))
            {
                return false;
            }
        }

        return hasDigit;
    }

    /// <summary>
    ///     Распознаёт подпись колонки-распорки вида «8мм»: переводить в ней нечего.
    /// </summary>
    public static bool IsSpacerLabel(string text)
    {
        var compact = text.Replace(" ", string.Empty).Replace("\t", string.Empty);

        if (!compact.EndsWith("мм", StringComparison.CurrentCultureIgnoreCase))
        {
            return false;
        }

        var digits = compact[..^2];

        return digits.Length > 0 && digits.All(char.IsDigit);
    }

    private static bool SkipsWhitespaceInsideWord(string text, int hyphenIndex)
    {
        if (hyphenIndex is 0 || hyphenIndex + 1 >= text.Length)
        {
            return false;
        }

        if (text[hyphenIndex + 1] is not (' ' or '\t'))
        {
            return false;
        }

        var next = SkipWhitespace(text, hyphenIndex + 1);

        return next < text.Length && char.IsLetter(text[next]) && char.IsLetter(text[hyphenIndex - 1]);
    }

    private static int SkipWhitespace(string text, int index)
    {
        while (index < text.Length && text[index] is ' ' or '\t')
        {
            index++;
        }

        return index;
    }
}
