using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
                RejectedTexts = [],
                Failures = []
            };
        }

        var translations = new Dictionary<string, string>(StringComparer.CurrentCulture);
        var failures = new List<string>();
        var rejected = new List<string>();

        foreach (var batch in Batch(pending, batchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var texts = batch.Select(entry => entry.Readable).ToList();
                var translated = await translator.TranslateAsync(texts, cancellationToken).ConfigureAwait(false);

                for (var index = 0; index < batch.Count; index++)
                {
                    var source = batch[index].Readable;
                    var result = translated[index].Trim();

                    if (!PreservesNumbers(source, result))
                    {
                        rejected.Add(batch[index].Readable);
                        continue;
                    }

                    translations[batch[index].Key] = result;
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
            RejectedTexts = rejected,
            Failures = failures
        };
    }

    /// <summary>
    ///     Сверяет числа исходной строки и перевода. Размеры и длины вида «Полоса стальная –40х5, L=1660»
    ///     машинный перевод иногда переставляет или теряет, а в спецификации это ошибка чертежа, поэтому
    ///     такой перевод не принимается и строка остаётся непереведённой.
    /// </summary>
    private static bool PreservesNumbers(string source, string translation)
    {
        var expected = ExtractNumbers(source);

        if (expected.Count is 0)
        {
            return true;
        }

        var actual = ExtractNumbers(translation);

        return expected.Count == actual.Count && expected.All(actual.Contains);
    }

    /// <summary>
    ///     Убирает разделитель тысяч: перевод пишет «1,660» там, где в оригинале «1660», и посимвольная
    ///     сверка сочла бы это потерей числа. Десятичная запятая не трогается — после неё не три цифры.
    /// </summary>
    private static string JoinThousands(string text)
    {
        var joined = new StringBuilder(text.Length);

        for (var index = 0; index < text.Length; index++)
        {
            if (IsThousandsSeparator(text, index))
            {
                continue;
            }

            joined.Append(text[index]);
        }

        return joined.ToString();
    }

    private static bool IsThousandsSeparator(string text, int index)
    {
        if (text[index] is not (',' or ' ' or ' ' or ' '))
        {
            return false;
        }

        if (index is 0 || !char.IsDigit(text[index - 1]))
        {
            return false;
        }

        var digits = 0;

        while (index + 1 + digits < text.Length && char.IsDigit(text[index + 1 + digits]))
        {
            digits++;
        }

        return digits is 3;
    }

    private static List<string> ExtractNumbers(string source)
    {
        var text = JoinThousands(source);
        var numbers = new List<string>();
        var current = new StringBuilder();

        foreach (var symbol in text)
        {
            if (char.IsDigit(symbol))
            {
                current.Append(symbol);
                continue;
            }

            if (current.Length > 0)
            {
                numbers.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            numbers.Add(current.ToString());
        }

        return numbers;
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
