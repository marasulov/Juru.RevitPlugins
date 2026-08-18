using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Juru.RevitPlugins.Schedules.Models;
using Juru.RevitPlugins.Schedules.Services.Translation;

namespace Juru.RevitPlugins.Schedules.Services;

/// <summary>
///     Заполняет пустые переводы в глоссарии машинным переводом. Уже заполненные строки не переводятся
///     повторно: ни выведенные из самой строки, ни исправленные человеком, ни переведённые ранее машиной.
///     Переводится читаемый вид, а не ключ, — иначе служба получит текст с ручными переносами.
/// </summary>
public sealed class GlossaryTranslator(ITextTranslator translator, int batchSize)
{
    public async Task<GlossaryTranslationResult> TranslateAsync(
        IReadOnlyList<GlossaryEntry> entries,
        CancellationToken cancellationToken)
    {
        if (entries is null)
        {
            throw new ArgumentNullException(nameof(entries));
        }

        var pending = entries
            .Where(entry => entry.Suggestion.Length is 0 && entry.Readable.Length > 0)
            .ToList();

        if (pending.Count is 0)
        {
            return new GlossaryTranslationResult
            {
                Entries = entries,
                TranslatedCount = 0,
                Failures = []
            };
        }

        var translations = new Dictionary<string, string>(StringComparer.CurrentCulture);
        var failures = new List<string>();

        foreach (var batch in Batch(pending, batchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var texts = batch.Select(entry => entry.Readable).ToList();
                var translated = await translator.TranslateAsync(texts, cancellationToken).ConfigureAwait(false);

                for (var index = 0; index < batch.Count; index++)
                {
                    translations[batch[index].Key] = translated[index].Trim();
                }
            }
            catch (TranslationException exception)
            {
                failures.Add(exception.Message);
            }
        }

        return new GlossaryTranslationResult
        {
            Entries = Apply(entries, translations),
            TranslatedCount = translations.Count,
            Failures = failures
        };
    }

    private static IReadOnlyList<GlossaryEntry> Apply(
        IReadOnlyList<GlossaryEntry> entries,
        IReadOnlyDictionary<string, string> translations)
    {
        return entries
            .Select(entry => translations.TryGetValue(entry.Key, out var translation) && translation.Length > 0
                ? entry with { Suggestion = translation, Source = TranslationSource.Machine }
                : entry)
            .ToList();
    }

    private static IEnumerable<List<GlossaryEntry>> Batch(IReadOnlyList<GlossaryEntry> entries, int size)
    {
        for (var start = 0; start < entries.Count; start += size)
        {
            yield return entries.Skip(start).Take(size).ToList();
        }
    }
}
