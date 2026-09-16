using System;
using System.Linq;
using System.Text;

namespace Juru.RevitPlugins.Schedules.Services;

/// <summary>
///     Разбор русского текста спецификаций: что подлежит переводу и как показать это переводчику.
/// </summary>
public static class RussianText
{
    /// <summary>
    ///     Есть ли в строке буквы. Отбор переводимого идёт по этому, а не по кириллице: у эталона
    ///     условие — просто непустая строка, и требование кириллицы молча опустошало вкладки там,
    ///     где текст набран латиницей или смешанно. Строка без букв («1», «-», «100х200») перевода
    ///     не требует.
    /// </summary>
    public static bool ContainsLetters(string? text)
    {
        if (text is null)
        {
            return false;
        }

        foreach (var symbol in text)
        {
            if (char.IsLetter(symbol))
            {
                return true;
            }
        }

        return false;
    }

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
    ///     «Кат. поме-  ще-  ния» превращается в «Кат. помещения». Переносы строк сохраняются — кроме тех,
    ///     что стоят сразу за дефисом: там перенесено слово, а не строка.
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

    /// <summary>Как строка поделена: чем, в каком по счёту месте и с какой стороны русский.</summary>
    public readonly record struct BilingualShape(bool SourceOnLeft, char Separator, int Ordinal);

    /// <summary>
    ///     Разбирает уже двуязычную строку на исходник и перевод: «Кабинет / Office» даёт «Кабинет»
    ///     и «Office». <c>false</c>, если строка одноязычная.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Нужно потому, что двуязычная запись возвращается в модель: в этом режиме плагин пишет
    ///         в параметр «исходник — перевод», и при следующем чтении такая строка приходит целиком.
    ///         Без разбора она легла бы в словарь отдельным ключом, где в «исходном тексте» стоял бы и
    ///         русский, и английский, а с каждым прогоном такие ключи ещё и множились бы.
    ///     </para>
    ///     <para>
    ///         Половины бывают многострочными: текст ячейки переносят руками, и разделитель тогда
    ///         стоит посреди переносов, а то и один на всей строке. Поэтому перебираются все места,
    ///         где разделитель встретился, а не первое попавшееся.
    ///     </para>
    ///     <para>
    ///         Кириллица требуется ровно с одной стороны: по языку и видно, где исходник, а где
    ///         перевод. Порядок любой — «Кабинет / Office» пишем мы, «Heating / Отопление» так
    ///         названо в самой модели.
    ///     </para>
    /// </remarks>
    public static bool TrySplitBilingual(string text, out string source, out string translation)
    {
        return TrySplitBilingual(text, out source, out translation, out _);
    }

    /// <param name="shape">
    ///     Как прошёл разрыв. Нужно, чтобы так же поделить перевод: пару переводили целиком, и в
    ///     переводе лежат обе половины.
    /// </param>
    public static bool TrySplitBilingual(
        string text,
        out string source,
        out string translation,
        out BilingualShape shape)
    {
        var trimmed = TrimSeparators(text);

        foreach (var separator in PairSeparators)
        {
            if (TrySplitAt(trimmed, separator, SeparatorPlace.Adjacent, out source, out translation, out shape))
            {
                return true;
            }
        }

        // У дефиса требование строже - пробел с обеих сторон, иначе распались бы «ДВ-2» и
        // «Юго-западный фасад».
        if (TrySplitAt(trimmed, '-', SeparatorPlace.Surrounded, out source, out translation, out shape))
        {
            return true;
        }

        return TrySplitAtLanguageBoundary(trimmed, out source, out translation, out shape);
    }

    /// <summary>
    ///     Берёт из строки ту половину, на месте которой в паре стоял русский исходник.
    /// </summary>
    /// <remarks>
    ///     Пару переводили целиком, поэтому в переводе оказались обе половины: «Cold consumption, W /
    ///     Cold consumption, W». Нужна та, что переведена с исходника, а какая это — видно по тому,
    ///     как поделился ключ.
    /// </remarks>
    public static bool TryTakeSameHalf(string text, BilingualShape shape, out string half)
    {
        half = string.Empty;

        if (shape.Separator is '\0')
        {
            return false;
        }

        var trimmed = TrimSeparators(text);
        var seen = 0;

        for (var index = 0; index < trimmed.Length; index++)
        {
            if (trimmed[index] != shape.Separator || seen++ != shape.Ordinal)
            {
                continue;
            }

            var left = TrimSeparators(trimmed[..index]);
            var right = TrimSeparators(trimmed[(index + 1)..]);

            if (left.Length is 0 || right.Length is 0)
            {
                return false;
            }

            half = shape.SourceOnLeft ? left : right;

            return true;
        }

        return false;
    }

    /// <summary>
    ///     Убирает разделители, оставшиеся по краям: «/ Ко-ч.» и «1 unit, kg /» — это половина пары,
    ///     у которой вторая половина оказалась пуста.
    /// </summary>
    public static string TrimSeparators(string text)
    {
        var trimmed = text.Trim();

        while (trimmed.Length > 0 && IsEdgeSeparator(trimmed[0]))
        {
            trimmed = trimmed[1..].Trim();
        }

        while (trimmed.Length > 0 && IsEdgeSeparator(trimmed[^1]))
        {
            trimmed = trimmed[..^1].Trim();
        }

        return trimmed;
    }

    /// <summary>Разделители пары, которые внутри слова не встречаются.</summary>
    private static readonly char[] PairSeparators = ['/', '—', '|', ';'];

    /// <summary>Где разделителю позволено стоять, чтобы считаться разделителем пары.</summary>
    private enum SeparatorPlace
    {
        /// <summary>Пробел или перенос хотя бы с одной стороны: «FM-200/ Помещение».</summary>
        Adjacent,

        /// <summary>Пробел с обеих сторон.</summary>
        Surrounded
    }

    private static bool IsEdgeSeparator(char symbol)
    {
        return symbol is '/' or '—' or '|';
    }

    /// <summary>
    ///     Делит там, где кончается один язык и начинается другой, а не по первому же переносу.
    /// </summary>
    /// <remarks>
    ///     Явного разделителя нет — половины разнесены только ручными переносами. По первому переносу
    ///     «Building (structure),\npremise name\nНаименование здания» разорвалось бы после первой
    ///     строки, и «premise name» уехало бы в исходник. Поэтому разрыв ищется у границы языка: на
    ///     ближайшем переносе перед первой кириллицей либо после последней.
    /// </remarks>
    private static bool TrySplitAtLanguageBoundary(
        string text,
        out string source,
        out string translation,
        out BilingualShape shape)
    {
        source = string.Empty;
        translation = string.Empty;
        shape = default;

        var first = IndexOfCyrillic(text, fromStart: true);

        if (first < 0)
        {
            return false;
        }

        var before = text.LastIndexOf('\n', first);

        if (before >= 0 && TryTakeHalves(text[..before], text[(before + 1)..], out source, out translation))
        {
            shape = new BilingualShape(SourceOnLeft: false, '\n', Count(text, '\n', before));

            return true;
        }

        var after = text.IndexOf('\n', IndexOfCyrillic(text, fromStart: false));

        if (after >= 0 && TryTakeHalves(text[..after], text[(after + 1)..], out source, out translation))
        {
            shape = new BilingualShape(SourceOnLeft: true, '\n', Count(text, '\n', after));

            return true;
        }

        return false;
    }

    private static int IndexOfCyrillic(string text, bool fromStart)
    {
        if (fromStart)
        {
            for (var index = 0; index < text.Length; index++)
            {
                if (text[index] is >= 'Ѐ' and <= 'ӿ')
                {
                    return index;
                }
            }

            return -1;
        }

        for (var index = text.Length - 1; index >= 0; index--)
        {
            if (text[index] is >= 'Ѐ' and <= 'ӿ')
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>Сколько раз символ встретился до этого места: по тому же счёту делится и перевод.</summary>
    private static int Count(string text, char symbol, int before)
    {
        var count = 0;

        for (var index = 0; index < before; index++)
        {
            if (text[index] == symbol)
            {
                count++;
            }
        }

        return count;
    }

    private static bool TrySplitAt(
        string text,
        char separator,
        SeparatorPlace place,
        out string source,
        out string translation,
        out BilingualShape shape)
    {
        source = string.Empty;
        translation = string.Empty;
        shape = default;

        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != separator || !Fits(text, index, place))
            {
                continue;
            }

            if (!TryTakeHalves(text[..index], text[(index + 1)..], out source, out translation))
            {
                continue;
            }

            shape = new BilingualShape(
                SourceOnLeft: ContainsCyrillic(text[..index]),
                separator,
                Count(text, separator, index));

            return true;
        }

        return false;
    }

    private static bool Fits(string text, int index, SeparatorPlace place)
    {
        var before = index > 0 && IsSpace(text[index - 1]);
        var after = index + 1 < text.Length && IsSpace(text[index + 1]);

        return place is SeparatorPlace.Surrounded ? before && after : before || after;
    }

    private static bool IsSpace(char symbol)
    {
        return symbol is ' ' or '\t' or '\n' or '\r';
    }

    /// <summary>
    ///     Принимает половины, если кириллица есть ровно в одной из них: русская идёт исходником,
    ///     вторая — переводом. Разделители по краям половин снимаются заодно.
    /// </summary>
    private static bool TryTakeHalves(string left, string right, out string source, out string translation)
    {
        source = string.Empty;
        translation = string.Empty;

        var first = TrimSeparators(left);
        var second = TrimSeparators(right);

        if (first.Length is 0 || second.Length is 0)
        {
            return false;
        }

        var firstIsRussian = ContainsCyrillic(first);

        if (firstIsRussian == ContainsCyrillic(second))
        {
            return false;
        }

        var russian = firstIsRussian ? first : second;
        var other = firstIsRussian ? second : first;

        if (!ContainsLetters(other))
        {
            return false;
        }

        source = russian;
        translation = other;

        return true;
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
    /// <summary>
    ///     Однобуквенное обозначение: «П», «В», «К», «А». В спецификациях это марки систем и трубопроводов,
    ///     а не слова, и машинный перевод делает из них «In», «To», «A».
    /// </summary>
    /// <remarks>
    ///     Считается однобуквенным и «Т.», и «П:» - знак препинания при букве обозначения не меняет.
    /// </remarks>
    public static bool IsSingleLetterMark(string text)
    {
        var letters = 0;

        foreach (var symbol in text.Trim())
        {
            if (char.IsLetter(symbol))
            {
                letters++;

                if (letters > 1)
                {
                    return false;
                }

                continue;
            }

            if (char.IsDigit(symbol))
            {
                return false;
            }
        }

        return letters is 1;
    }

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

        // Перевод строки считается наравне с пробелом: в узкой колонке слово переносят и так.
        if (text[hyphenIndex + 1] is not (' ' or '\t' or '\n' or '\r'))
        {
            return false;
        }

        var next = SkipWhitespace(text, hyphenIndex + 1);

        return next < text.Length && char.IsLetter(text[next]) && char.IsLetter(text[hyphenIndex - 1]);
    }

    private static int SkipWhitespace(string text, int index)
    {
        while (index < text.Length && text[index] is ' ' or '\t' or '\n' or '\r')
        {
            index++;
        }

        return index;
    }
}
