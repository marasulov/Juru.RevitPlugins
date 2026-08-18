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

    public const int KeyColumn = 0;
    public const int ReadableColumn = 1;
    public const int TranslationColumn = 2;

    private const int MinimumColumnCount = 3;

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
            "Спецификации"));

        foreach (var entry in entries)
        {
            text.AppendLine(DelimitedText.BuildRow(
                Delimiter,
                entry.Key,
                entry.Readable,
                entry.Suggestion,
                entry.Occurrences.ToString(),
                string.Join(" | ", entry.Schedules)));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Excel читает CSV как ANSI, пока в начале файла нет метки UTF-8.
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(true));
    }

    /// <summary>
    ///     Читает пары «ключ — перевод», пропуская заголовок и строки без перевода.
    /// </summary>
    public static Dictionary<string, string> Read(string path)
    {
        var translations = new Dictionary<string, string>(StringComparer.CurrentCulture);
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

            translations[key] = translation;
        }

        return translations;
    }
}
