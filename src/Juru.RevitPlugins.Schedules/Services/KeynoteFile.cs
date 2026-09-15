using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Autodesk.Revit.DB;

namespace Juru.RevitPlugins.Schedules.Services;

/// <summary>
///     Одна запись таблицы ключевых примечаний: ключ и описание.
/// </summary>
public sealed record KeynoteEntry(string Key, string Description);

/// <summary>
///     Таблица ключевых примечаний. Она живёт во внешнем tab-разделённом файле, а не в элементах
///     модели, поэтому переводится перезаписью файла.
/// </summary>
/// <remarks>
///     <para>
///         Перевод пишется в **отдельный** файл рядом с исходным — <c>keynotes_EN.txt</c>, — а модель
///         переключается на него. Русский файл при этом цел: он общий для всех моделей, которые на
///         него ссылаются, и перезапись сделала бы русский комплект английским заодно.
///     </para>
///     <para>
///         Файл Ревит пишет в UTF-16, поэтому и читается он так же: в UTF-8 описания превратятся
///         в мусор. Пустые строки и строки-комментарии переносятся без изменений.
///     </para>
/// </remarks>
public sealed class KeynoteFile
{
    private const char Separator = '\t';
    private const string CommentMark = "#";

    private KeynoteFile(string path, IReadOnlyList<KeynoteEntry> entries)
    {
        Path = path;
        Entries = entries;
    }

    /// <summary>Путь исходного файла с русскими описаниями.</summary>
    public string Path { get; }

    public IReadOnlyList<KeynoteEntry> Entries { get; }

    /// <summary>
    ///     Читает таблицу, на которую ссылается модель. <c>null</c> — таблица не назначена или файл
    ///     недоступен.
    /// </summary>
    public static KeynoteFile? TryRead(Document document)
    {
        var path = TryResolvePath(document);

        if (path is null || !File.Exists(path))
        {
            return null;
        }

        var entries = new List<KeynoteEntry>();

        foreach (var line in File.ReadAllLines(path, Encoding.Unicode))
        {
            if (!TrySplit(line, out var columns))
            {
                continue;
            }

            entries.Add(new KeynoteEntry(columns[0].Trim(), columns[1]));
        }

        return new KeynoteFile(path, entries);
    }

    /// <summary>
    ///     Пишет переведённую таблицу рядом с исходной и переключает на неё модель. Возвращает путь
    ///     нового файла и число заменённых описаний.
    /// </summary>
    /// <param name="translations">Перевод по ключу примечания. Ключи без перевода остаются русскими.</param>
    /// <param name="languageCode">Код языка для имени файла: <c>keynotes_EN.txt</c>.</param>
    public (string Path, int Count) WriteTranslated(
        Document document,
        IReadOnlyDictionary<string, string> translations,
        string languageCode)
    {
        if (translations is null)
        {
            throw new ArgumentNullException(nameof(translations));
        }

        var directory = System.IO.Path.GetDirectoryName(Path) ?? string.Empty;
        var name = System.IO.Path.GetFileNameWithoutExtension(Path);
        var extension = System.IO.Path.GetExtension(Path);
        var target = System.IO.Path.Combine(directory, $"{name}_{languageCode.ToUpperInvariant()}{extension}");

        var lines = new List<string>();
        var replaced = 0;

        foreach (var line in File.ReadAllLines(Path, Encoding.Unicode))
        {
            if (!TrySplit(line, out var columns))
            {
                lines.Add(line);
                continue;
            }

            if (!translations.TryGetValue(columns[0].Trim(), out var translation) || translation.Length is 0)
            {
                lines.Add(line);
                continue;
            }

            columns[1] = translation;

            lines.Add(string.Join(Separator.ToString(), columns));
            replaced++;
        }

        File.WriteAllLines(target, lines, Encoding.Unicode);

        Reload(document, target);

        return (target, replaced);
    }

    /// <summary>
    ///     Путь исходной таблицы. Если модель уже переключена на переведённую, возвращается базовый
    ///     файл: иначе следующий прогон переводил бы английские описания.
    /// </summary>
    private static string? TryResolvePath(Document document)
    {
        var table = KeynoteTable.GetKeynoteTable(document);
        var reference = table?.GetExternalFileReference();

        if (reference is null || reference.GetLinkedFileStatus() is LinkedFileStatus.Invalid)
        {
            return null;
        }

        var modelPath = reference.GetAbsolutePath();

        if (modelPath is null)
        {
            return null;
        }

        var path = ModelPathUtils.ConvertModelPathToUserVisiblePath(modelPath);

        return TryStripLanguage(path) ?? path;
    }

    /// <summary>Отбрасывает суффикс языка из имени файла, если базовый файл рядом существует.</summary>
    private static string? TryStripLanguage(string path)
    {
        var directory = System.IO.Path.GetDirectoryName(path) ?? string.Empty;
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        var extension = System.IO.Path.GetExtension(path);
        var underscore = name.LastIndexOf('_');

        if (underscore <= 0 || name.Length - underscore > 6)
        {
            return null;
        }

        var candidate = System.IO.Path.Combine(directory, name.Substring(0, underscore) + extension);

        return File.Exists(candidate) ? candidate : null;
    }

    /// <summary>Строка с ключом и описанием. Ложь — пустая строка, комментарий или строка без описания.</summary>
    private static bool TrySplit(string line, out string[] columns)
    {
        columns = [];

        if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith(CommentMark, StringComparison.Ordinal))
        {
            return false;
        }

        var split = line.Split(Separator);

        if (split.Length < 2)
        {
            return false;
        }

        columns = split;

        return true;
    }

    private static void Reload(Document document, string path)
    {
        var modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(path);

        var resource = ExternalResourceReference.CreateLocalResource(
            document,
            ExternalResourceTypes.BuiltInExternalResourceTypes.KeynoteTable,
            modelPath,
            PathType.Absolute);

        using var transaction = new Transaction(document, "Английская таблица ключевых примечаний");

        transaction.Start();

        KeynoteTable.GetKeynoteTable(document).LoadFrom(resource, new KeyBasedTreeEntriesLoadResults());

        transaction.Commit();
    }
}
