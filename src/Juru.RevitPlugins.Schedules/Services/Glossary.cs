using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Juru.RevitPlugins.Schedules.Services;

/// <summary>
///     Русско-английский словарь, заполняемый переводчиком в том же CSV, который выдаёт аудит.
///     Ключ — исходная строка, значение — перевод.
/// </summary>
public sealed class Glossary
{
    private const char Separator = ';';
    private const int SourceColumn = 0;
    private const int TranslationColumn = 1;

    private readonly Dictionary<string, string> translations;
    private readonly HashSet<string> misses = new(StringComparer.CurrentCulture);

    private Glossary(Dictionary<string, string> translations, string? sourcePath)
    {
        this.translations = translations;
        SourcePath = sourcePath;
    }

    /// <summary>Путь загруженного файла; <c>null</c>, если словаря нет и текст остаётся русским.</summary>
    public string? SourcePath { get; }

    public int TranslationCount
    {
        get { return translations.Count; }
    }

    /// <summary>Русские строки, которых не оказалось в словаре.</summary>
    public IReadOnlyCollection<string> Misses
    {
        get { return misses; }
    }

    public static string DefaultPath
    {
        get
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "JuruPlugins",
                "Glossary",
                "glossary.csv");
        }
    }

    public static Glossary Load(string path)
    {
        if (!File.Exists(path))
        {
            return new Glossary([], null);
        }

        var translations = new Dictionary<string, string>(StringComparer.CurrentCulture);

        foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
        {
            var cells = ParseCsvLine(line);

            if (cells.Count <= TranslationColumn)
            {
                continue;
            }

            var source = cells[SourceColumn].Trim();
            var translation = cells[TranslationColumn].Trim();

            if (source.Length is 0 || translation.Length is 0)
            {
                continue;
            }

            translations[source] = translation;
        }

        return new Glossary(translations, path);
    }

    /// <summary>
    ///     Возвращает перевод, а при его отсутствии — исходный текст. Ненайденные русские строки
    ///     запоминаются и попадают в отчёт.
    /// </summary>
    public string Translate(string text)
    {
        var key = text.Trim();

        if (translations.TryGetValue(key, out var translation))
        {
            return translation;
        }

        if (RussianText.ContainsCyrillic(key))
        {
            misses.Add(key);
        }

        return text;
    }

    private static IReadOnlyList<string> ParseCsvLine(string line)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < line.Length; index++)
        {
            var symbol = line[index];

            if (symbol is '"')
            {
                if (inQuotes && index + 1 < line.Length && line[index + 1] is '"')
                {
                    cell.Append('"');
                    index++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (symbol == Separator && !inQuotes)
            {
                cells.Add(cell.ToString());
                cell.Clear();
                continue;
            }

            cell.Append(symbol);
        }

        cells.Add(cell.ToString());

        return cells;
    }
}
