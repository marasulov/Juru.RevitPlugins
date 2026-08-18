using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Juru.RevitPlugins.Schedules.Models;

namespace Juru.RevitPlugins.Schedules.Services;

/// <summary>
///     Выписывает строки, которых не оказалось в глоссарии, в том же формате, что и сам глоссарий: файл
///     заполняют переводом и вливают в глоссарий, ничего не перенабирая.
/// </summary>
public static class UntranslatedWriter
{
    public static string? Write(Glossary glossary, string reportDirectory)
    {
        if (glossary is null)
        {
            throw new ArgumentNullException(nameof(glossary));
        }

        if (glossary.Misses.Count is 0)
        {
            return null;
        }

        var entries = glossary.Misses
            .Select(miss => new GlossaryEntry
            {
                Key = miss.Key,
                Readable = RussianText.Normalize(miss.Key),
                Suggestion = string.Empty,
                Source = TranslationSource.None,
                Occurrences = miss.Value,
                Schedules = []
            })
            .OrderByDescending(entry => entry.Occurrences)
            .ThenBy(entry => entry.Readable, StringComparer.CurrentCulture)
            .ToList();

        var reportPath = Path.Combine(reportDirectory, "untranslated.csv");
        GlossaryFile.Write(entries, reportPath);

        return reportPath;
    }
}
