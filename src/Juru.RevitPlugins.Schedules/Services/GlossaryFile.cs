using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Juru.RevitPlugins.Schedules.Models;

namespace Juru.RevitPlugins.Schedules.Services;

/// <summary>
///     Формат файла глоссария. Запись и чтение живут рядом, чтобы номера колонок не разошлись.
/// </summary>
public static class GlossaryFile
{
    public const char Delimiter = ';';

    private const int KeyColumn = 0;
    private const int ReadableColumn = 1;
    private const int TranslationColumn = 2;
    private const int OccurrencesColumn = 3;
    private const int SchedulesColumn = 4;
    private const int SourceColumn = 5;
    private const int MinimumColumnCount = 3;

    private const string KeyHeader = "Ключ";
    private const string BaseMark = "базовый";
    private const string MachineMark = "машинный";
    private const string DerivedMark = "выведен";
    private const string HumanMark = "вручную";

    public static void Write(IReadOnlyList<GlossaryEntry> entries, string path)
    {
        if (entries is null)
        {
            throw new ArgumentNullException(nameof(entries));
        }

        var text = new StringBuilder();

        text.AppendLine(DelimitedText.BuildRow(
            Delimiter,
            "Ключ",
            "Читаемый вид",
            "Перевод",
            "Встречается",
            "Спецификации",
            "Источник"));

        foreach (var entry in entries)
        {
            text.AppendLine(DelimitedText.BuildRow(
                Delimiter,
                entry.Key,
                entry.Readable,
                entry.Suggestion,
                entry.Occurrences.ToString(),
                string.Join(" | ", entry.Schedules),
                DescribeSource(entry.Source)));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Excel читает CSV как ANSI, пока в начале файла нет метки UTF-8.
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(true));
    }

    /// <summary>
    ///     Читает пары «ключ — перевод» для подстановки, пропуская заголовок и строки без перевода.
    /// </summary>
    public static Dictionary<string, string> Read(string path)
    {
        var translations = new Dictionary<string, string>(StringComparer.CurrentCulture);

        foreach (var pair in ReadEntries(path))
        {
            translations[pair.Key] = pair.Value.Text;
        }

        return translations;
    }

    /// <summary>
    ///     Читает файл целиком, вместе со строками без перевода.
    /// </summary>
    /// <remarks>
    ///     <see cref="ReadEntries" /> строки без перевода пропускает - для подстановки они бесполезны.
    ///     Окну словаря нужны как раз они: непереведённая строка там и ждёт, чтобы её перевели руками.
    /// </remarks>
    public static IReadOnlyList<GlossaryEntry> ReadAll(string path)
    {
        var entries = new List<GlossaryEntry>();

        if (!File.Exists(path))
        {
            return entries;
        }

        var rows = DelimitedText.Parse(File.ReadAllText(path, Encoding.UTF8), Delimiter);

        foreach (var row in rows)
        {
            if (row.Count is 0)
            {
                continue;
            }

            var key = row[KeyColumn].Trim();

            if (key.Length is 0 || string.Equals(key, KeyHeader, StringComparison.CurrentCulture))
            {
                continue;
            }

            var translation = Column(row, TranslationColumn);
            var readable = Column(row, ReadableColumn);

            if (Reduce(ref key, ref translation))
            {
                // Ключ изменился, и прежний читаемый вид относился к паре целиком.
                readable = string.Empty;
            }

            entries.Add(new GlossaryEntry
            {
                Key = key,
                Readable = readable.Length > 0 ? readable : RussianText.Normalize(key),
                Suggestion = translation,
                Source = ParseSource(Column(row, SourceColumn)),
                Occurrences = int.TryParse(Column(row, OccurrencesColumn), out var count) ? count : 0,
                Schedules = Split(Column(row, SchedulesColumn))
            });
        }

        // После приведения одинаковые ключи могли сойтись: побеждает тот, у которого перевод есть.
        return entries
            .GroupBy(entry => entry.Key, StringComparer.CurrentCulture)
            .Select(group => group.FirstOrDefault(entry => entry.Suggestion.Length > 0) ?? group.First())
            .ToList();
    }

    /// <summary>
    ///     Приводит строку словаря к виду «русский ключ — только перевод» и сообщает, менялось ли
    ///     что-нибудь.
    /// </summary>
    /// <remarks>
    ///     В словарь попадали строки, уже записанные в модель двуязычными: ключом стояло «Кабинет /
    ///     Office», то есть исходник вместе с переводом. Такие строки чинятся при чтении, а не
    ///     разовым скриптом по файлу: словарь общий, лежит у каждого свой, и починить его должен сам
    ///     плагин - своим же разбором, а не догадками о разделителях в CSV. Порядок половин любой:
    ///     «Кабинет / Office» пишем мы, «Heating / Отопление» так названо в модели.
    /// </remarks>
    private static bool Reduce(ref string key, ref string translation)
    {
        // Края чистятся в любом случае: «/ Ко-ч.» и «1 unit, kg /» - это половина пары, у которой
        // вторая половина была пуста, и разделитель от неё остался висеть.
        var reducedKey = RussianText.TrimSeparators(key);
        var reducedTranslation = RussianText.TrimSeparators(translation);

        if (RussianText.TrySplitBilingual(reducedKey, out var source, out var written, out var shape))
        {
            reducedKey = source;

            if (reducedTranslation.Length is 0)
            {
                reducedTranslation = written;
            }
            else if (RussianText.TryTakeSameHalf(reducedTranslation, shape, out var half))
            {
                // Пару переводили целиком, и в переводе лежат обе половины. Нужна та, что стоит на
                // месте русской: только она переведена с исходника.
                reducedTranslation = half;
            }
            else if (RussianText.TrySplitBilingual(reducedTranslation, out _, out var english))
            {
                reducedTranslation = english;
            }
        }
        else if (RussianText.TrySplitBilingual(reducedTranslation, out _, out var only))
        {
            reducedTranslation = only;
        }

        var changed = !string.Equals(reducedKey, key, StringComparison.CurrentCulture)
                      || !string.Equals(reducedTranslation, translation, StringComparison.CurrentCulture);

        key = reducedKey;
        translation = reducedTranslation;

        return changed;
    }

    private static string Column(IReadOnlyList<string> row, int index)
    {
        return index < row.Count ? row[index].Trim() : string.Empty;
    }

    private static IReadOnlyList<string> Split(string joined)
    {
        return joined.Length is 0
            ? []
            : joined.Split('|').Select(part => part.Trim()).Where(part => part.Length > 0).ToList();
    }

    /// <summary>
    ///     Читает переводы вместе с происхождением: при пересборке глоссария оно переносится, чтобы
    ///     исправленное человеком не выглядело машинным.
    /// </summary>
    public static Dictionary<string, GlossaryTranslation> ReadEntries(string path)
    {
        var entries = new Dictionary<string, GlossaryTranslation>(StringComparer.CurrentCulture);

        if (!File.Exists(path))
        {
            return entries;
        }

        var rows = DelimitedText.Parse(File.ReadAllText(path, Encoding.UTF8), Delimiter);

        foreach (var row in rows)
        {
            if (row.Count < MinimumColumnCount)
            {
                continue;
            }

            var key = row[KeyColumn].Trim();
            var translation = row[TranslationColumn].Trim();

            Reduce(ref key, ref translation);

            if (key.Length is 0 || translation.Length is 0)
            {
                continue;
            }

            if (string.Equals(key, KeyHeader, StringComparison.CurrentCulture))
            {
                continue;
            }

            var source = row.Count > SourceColumn ? ParseSource(row[SourceColumn]) : TranslationSource.Human;

            entries[key] = new GlossaryTranslation(translation, source);
        }

        return entries;
    }

    private static string DescribeSource(TranslationSource source)
    {
        return source switch
        {
            TranslationSource.Machine => MachineMark,
            TranslationSource.Base => BaseMark,
            TranslationSource.Derived => DerivedMark,
            TranslationSource.Human => HumanMark,
            _ => string.Empty
        };
    }

    /// <summary>
    ///     Пустой или неизвестный источник считается ручным: так правка, внесённая в файл без пометки,
    ///     не будет затёрта машинным переводом.
    /// </summary>
    private static TranslationSource ParseSource(string mark)
    {
        var trimmed = mark.Trim();

        if (string.Equals(trimmed, MachineMark, StringComparison.CurrentCultureIgnoreCase))
        {
            return TranslationSource.Machine;
        }

        if (string.Equals(trimmed, DerivedMark, StringComparison.CurrentCultureIgnoreCase))
        {
            return TranslationSource.Derived;
        }

        if (string.Equals(trimmed, BaseMark, StringComparison.CurrentCultureIgnoreCase))
        {
            return TranslationSource.Base;
        }

        return TranslationSource.Human;
    }
}
