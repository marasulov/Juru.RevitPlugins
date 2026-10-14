using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Juru.RevitPlugins.ProjectSetup.Services;

/// <summary>Результат выбора шаблона в папке: файл, если выбор однозначен, иначе — из чего выбирать.</summary>
internal sealed class TemplateChoice
{
    public string? Path { get; init; }
    public List<string> Candidates { get; init; } = [];
    public string Note { get; init; } = "";
}

/// <summary>
/// Подбирает .rte в папке дисциплины. Имена файлов в папках шаблонов меняются, а сами папки —
/// нет, поэтому по умолчанию хранится папка, а файл выбирается правилом:
/// не резервная копия (<c>.0001.rte</c>) и не старая (<c>.old</c>); из них — тот, в имени
/// которого версия Revit (<c>V24</c>); нет версионных и файл один — он. Иначе выбирает человек.
/// </summary>
internal static class TemplateResolver
{
    private static readonly Regex Backup = new(@"\.\d{4}\.rte$", RegexOptions.IgnoreCase);

    public static TemplateChoice Resolve(string? folder, string revitVersion)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return new TemplateChoice { Note = "нет папки по умолчанию — выберите файл" };
        if (!Directory.Exists(folder))
            return new TemplateChoice { Note = $"папка не найдена: {folder}" };

        var candidates = Directory.GetFiles(folder!, "*.rte")
            .Where(f => !Backup.IsMatch(f) && f.IndexOf(".old", StringComparison.OrdinalIgnoreCase) < 0)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (candidates.Count == 0)
            return new TemplateChoice { Note = "в папке нет шаблонов .rte" };

        string version = "V" + (revitVersion.Length >= 2 ? revitVersion.Substring(revitVersion.Length - 2) : revitVersion);
        var versioned = candidates.Where(f => System.IO.Path.GetFileName(f).IndexOf(version, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

        if (versioned.Count == 1)
            return new TemplateChoice { Path = versioned[0], Candidates = candidates, Note = "выбран по версии " + version };
        if (versioned.Count == 0 && candidates.Count == 1)
            return new TemplateChoice { Path = candidates[0], Candidates = candidates, Note = "единственный в папке" };

        var options = versioned.Count > 1 ? versioned : candidates;
        return new TemplateChoice
        {
            Candidates = options,
            Note = $"несколько подходящих ({options.Count}) — выберите: " + string.Join("; ", options.Select(System.IO.Path.GetFileName))
        };
    }
}
