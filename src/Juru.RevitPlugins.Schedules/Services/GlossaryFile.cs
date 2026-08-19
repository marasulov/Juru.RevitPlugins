using System;
using System.Collections.Generic;
using System.IO;
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
    private const int TranslationColumn = 2;
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
