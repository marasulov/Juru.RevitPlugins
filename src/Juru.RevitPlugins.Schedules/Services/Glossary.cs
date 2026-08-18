using System;
using System.Collections.Generic;
using System.IO;

namespace Juru.RevitPlugins.Schedules.Services;

/// <summary>
///     Русско-английский словарь, заполняемый переводчиком в том же CSV, который выдаёт сбор глоссария.
///     Подстановка идёт по точному совпадению: изменённая в модели строка перестаёт совпадать и попадает
///     в непереведённые, поэтому устаревший перевод не остаётся незамеченным.
/// </summary>
public sealed class Glossary
{
    private readonly Dictionary<string, string> translations;
    private readonly Dictionary<string, int> misses = new(StringComparer.CurrentCulture);

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

    /// <summary>Русские строки, которых не оказалось в словаре, и сколько раз они встретились.</summary>
    public IReadOnlyDictionary<string, int> Misses
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

        return new Glossary(GlossaryFile.Read(path), path);
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
            misses.TryGetValue(key, out var count);
            misses[key] = count + 1;
        }

        return text;
    }

    /// <summary>
    ///     Ищет перевод, не записывая промах: используется при пересборке глоссария, чтобы перенести уже
    ///     сделанные переводы.
    /// </summary>
    public bool TryFind(string key, out string translation)
    {
        return translations.TryGetValue(key.Trim(), out translation!);
    }
}
