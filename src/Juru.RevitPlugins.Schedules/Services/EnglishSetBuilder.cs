using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Juru.RevitPlugins.Schedules.Models;
using Juru.RevitPlugins.Schedules.Services.Categories;
using Juru.RevitPlugins.Schedules.Services.Translation;

using Juru.RevitPlugins.Schedules.Localization;

namespace Juru.RevitPlugins.Schedules.Services;

/// <summary>
///     Собирает английский комплект по тому, что отмечено в окне: сохраняет вычитанные переводы
///     в глоссарий, добирает недостающие машиной, перечитывает отмеченные спецификации целиком и
///     рисует их копии, а затем записывает переводы категорий модели.
/// </summary>
/// <remarks>
///     Порядок важен: спецификации перечитываются первыми, их текст дописывается в глоссарий, и
///     только потом идёт отрисовка — подстановка при отрисовке читает файл, а не память окна.
///     Перечитывание стоит здесь, а не при открытии окна: чтобы собрать текст спецификации целиком,
///     её надо выгрузить на диск, и семьдесят таких выгрузок делали открытие окна долгим, тогда как
///     нужны они только тому, кто нажал «Построить».
/// </remarks>
public sealed class EnglishSetBuilder(UIDocument uiDocument, SchedulesConfig config)
{
    /// <summary>Устойчивый ключ категории: её имя переводится и для отбора не годится.</summary>
    private const string KeynoteKey = "Keynotes";

    /// <param name="translations">Вычитанные в окне пары «исходный текст — перевод».</param>
    /// <param name="selected">Отмеченные спецификации, прочитанные окном облегчённо.</param>
    /// <param name="categories">Что записать в параметры элементов модели.</param>
    /// <param name="layout">Только перевод или исходник и перевод рядом.</param>
    /// <param name="mode">
    ///     Что сделать с отмеченными спецификациями: копия линиями, копия спецификацией или перевод в
    ///     самих спецификациях.
    /// </param>
    public EnglishSetResult Build(
        IReadOnlyList<GlossaryEntry> translations,
        IReadOnlyList<ReadSchedule> selected,
        IReadOnlyList<CategoryWrite> categories,
        TranslationLayout layout,
        ScheduleCopyMode mode,
        IProgress<string>? progress = null)
    {
        if (translations is null)
        {
            throw new ArgumentNullException(nameof(translations));
        }

        if (selected is null)
        {
            throw new ArgumentNullException(nameof(selected));
        }

        if (categories is null)
        {
            throw new ArgumentNullException(nameof(categories));
        }

        var document = uiDocument.Document;
        var runStamp = DateTime.Now;
        var reportDirectory = ReportLocation.Create("EnglishSchedules", document.Title, runStamp);
        var glossaryPath = config.ResolveGlossaryPath();

        // Отмеченное перечитывается до глоссария: его текст в глоссарий и попадёт. Копия рисуется по
        // напечатанной таблице, а прочитанного окном для этого мало - там нет ни текста ячеек, ни
        // размеров строк.
        var printed = new SheetScheduleCopyService(uiDocument).ReadPrinted(
            selected.Select(schedule => schedule.Source).ToList(),
            reportDirectory,
            Step(progress, "Перечитываю отмеченные спецификации"));

        progress?.Report("Сохраняю глоссарий и перевожу недостающие строки");

        var collected = CollectScheduleTexts(printed, glossaryPath);
        var merged = SaveGlossary(
            translations.Concat(collected).ToList(),
            glossaryPath,
            reportDirectory,
            out var translated,
            out var note);

        var glossary = Glossary.Load(glossaryPath, layout);

        var drawn = mode is ScheduleCopyMode.RedrawAsLines
            ? Draw(printed, glossary, reportDirectory, progress)
            : CopyAsSchedules(printed, glossary, progress);

        // Ключевые примечания записываются перезаписью внешнего файла, а не в элементы модели,
        // поэтому идут отдельно от остальных категорий.
        var keynotes = categories.FirstOrDefault(category => category.Key is KeynoteKey);
        var elementCategories = categories.Where(category => category.Key is not KeynoteKey).ToList();

        var applied = Apply(document, elementCategories, glossary, layout, out var withoutTranslation);
        var keynoteResult = WriteKeynotes(document, keynotes, glossary, layout, progress);

        if (keynoteResult is not null)
        {
            applied = applied.Concat([keynoteResult.Value.Applied]).ToList();
        }

        var reportPath = drawn.Count is 0
            ? null
            : SheetScheduleCopyReportWriter.Write(drawn, document.Title, glossary, runStamp);

        return new EnglishSetResult
        {
            DrawnCount = drawn.Count(copy => copy.Error is null),
            FailedCount = drawn.Count(copy => copy.Error is not null),
            TranslatedNowCount = translated.TranslatedCount,
            UntranslatedCount = merged.Count(entry => entry.Suggestion.Length is 0),
            Categories = applied,
            WithoutTranslationCount = withoutTranslation,
            ReportPath = reportPath,
            KeynotePath = keynoteResult?.Path,
            TranslatorNote = note,
            Failures = translated.Failures
        };
    }

    /// <summary>
    ///     Кладёт вычитанное в окне в глоссарий, добирает пустые машиной и сохраняет файл. Строки
    ///     других моделей переносятся как есть: глоссарий общий, и перезапись только собранным
    ///     потеряла бы остальные.
    /// </summary>
    private IReadOnlyList<GlossaryEntry> SaveGlossary(
        IReadOnlyList<GlossaryEntry> collected,
        string glossaryPath,
        string reportDirectory,
        out GlossaryTranslationResult translated,
        out string note)
    {
        var existing = GlossaryFile.ReadEntries(glossaryPath);

        translated = Translate(collected, out note);

        var collectedKeys = translated.Entries
            .Select(entry => entry.Key)
            .ToHashSet(StringComparer.CurrentCulture);

        var carried = existing
            .Where(pair => !collectedKeys.Contains(pair.Key))
            .Select(pair => new GlossaryEntry
            {
                Key = pair.Key,
                Readable = RussianText.Normalize(pair.Key),
                Suggestion = pair.Value.Text,
                Source = pair.Value.Source,
                Occurrences = 0,
                Schedules = []
            });

        var merged = translated.Entries
            .Concat(carried)
            .OrderBy(entry => entry.Readable, StringComparer.CurrentCulture)
            .ToList();

        GlossaryFile.Write(merged, glossaryPath);
        GlossaryFile.Write(merged, Path.Combine(reportDirectory, "glossary.csv"));

        return merged;
    }

    /// <summary>
    ///     Переводит строки без перевода. Отсутствие настроенной службы не ошибка: строки остаются
    ///     пустыми, и на их месте в копии останется русский текст.
    /// </summary>
    private GlossaryTranslationResult Translate(IReadOnlyList<GlossaryEntry> entries, out string note)
    {
        var settings = config.Translation;
        var translator = TranslatorFactory.TryCreate(settings, out var reason);

        if (translator is null)
        {
            note = reason ?? "не настроена";

            return new GlossaryTranslationResult
            {
                Entries = entries,
                TranslatedCount = 0,
                RejectedTexts = [],
                Failures = []
            };
        }

        note = translator.Name;

        // Внешний командный интерфейс Revit синхронный, поэтому запросы выполняются на отдельном потоке.
        return Task.Run(() => new GlossaryTranslator(translator, settings.BatchSize)
                .TranslateAsync(entries, CancellationToken.None))
            .GetAwaiter()
            .GetResult();
    }

    private IReadOnlyList<SheetScheduleCopy> CopyAsSchedules(
        IReadOnlyList<ReadSchedule> printed,
        Glossary glossary,
        IProgress<string>? progress)
    {
        if (printed.Count is 0)
        {
            return [];
        }

        progress?.Report("Собираю английские спецификации");

        return new SheetScheduleCopyService(uiDocument)
            .CopyAllAsSchedules(printed, glossary, config.Translation.TargetLanguage);
    }

    private IReadOnlyList<SheetScheduleCopy> Draw(
        IReadOnlyList<ReadSchedule> printed,
        Glossary glossary,
        string reportDirectory,
        IProgress<string>? progress)
    {
        if (printed.Count is 0)
        {
            return [];
        }

        progress?.Report("Рисую английские копии");

        return new SheetScheduleCopyService(uiDocument).DrawAll(printed, glossary, reportDirectory);
    }

    /// <summary>
    ///     Строки таблицы для глоссария: под копию берётся весь её текст, в копии переводится каждая ячейка.
    /// </summary>
    private IReadOnlyList<GlossaryEntry> CollectScheduleTexts(
        IReadOnlyList<ReadSchedule> printed,
        string glossaryPath)
    {
        var existing = GlossaryFile.ReadEntries(glossaryPath);

        // Базовый словарь - общий на все проекты, он лежит рядом с плагином, а не в глоссарии
        // модели: глоссарий модели уже пришёл сюда как existing и сильнее базового.
        var baseTerms = ReadBaseTerms(config.ResolveBaseGlossaryPath());

        var collected = GlossaryCollector.Collect(printed, existing, baseTerms).Entries;

        return collected.Concat(ScheduleTitles(printed, collected, existing, baseTerms)).ToList();
    }

    /// <summary>
    ///     Названия таблиц. В снимке строка названия пуста, когда её не переопределяли руками: Ревит
    ///     рисует там имя вида, и в ячейке лежать нечему. Без этого шапка копии оставалась бы русской -
    ///     переводить было бы нечего, потому что в глоссарий название не попадало.
    /// </summary>
    private static IReadOnlyList<GlossaryEntry> ScheduleTitles(
        IReadOnlyList<ReadSchedule> printed,
        IReadOnlyList<GlossaryEntry> collected,
        IReadOnlyDictionary<string, GlossaryTranslation> existing,
        IReadOnlyDictionary<string, string> baseTerms)
    {
        var known = collected.Select(entry => entry.Key).ToHashSet(StringComparer.CurrentCulture);

        return printed
            .Select(schedule => ScheduleCopyName.TitleOf(schedule.Source.Schedule.Name).Trim())
            .Where(title => title.Length > 0 && RussianText.ContainsLetters(title))
            .Distinct(StringComparer.CurrentCulture)
            .Where(title => !known.Contains(title))
            .Select(title => Entry(title, existing, baseTerms))
            .ToList();
    }

    /// <summary>Строка глоссария по тем же правилам, что и у текста таблицы.</summary>
    private static GlossaryEntry Entry(
        string text,
        IReadOnlyDictionary<string, GlossaryTranslation> existing,
        IReadOnlyDictionary<string, string> baseTerms)
    {
        var readable = RussianText.Normalize(text);
        var translation = GlossaryCollector.Resolve(text, readable, existing, baseTerms);

        return new GlossaryEntry
        {
            Key = text,
            Readable = readable,
            Suggestion = translation.Text,
            Source = translation.Source,
            Occurrences = 0,
            Schedules = []
        };
    }

    /// <summary>Общий словарь повторяющихся терминов: проверяется после глоссария проекта.</summary>
    private static IReadOnlyDictionary<string, string> ReadBaseTerms(string path)
    {
        var terms = new Dictionary<string, string>(StringComparer.CurrentCulture);

        foreach (var pair in GlossaryFile.ReadEntries(path))
        {
            terms[RussianText.Normalize(pair.Key)] = pair.Value.Text;
        }

        return terms;
    }

    /// <summary>Сообщает о шаге и дальше пересылает ход работы как есть.</summary>
    private static IProgress<string>? Step(IProgress<string>? progress, string note)
    {
        progress?.Report(note);

        return progress;
    }

    /// <summary>
    ///     Пишет переведённую таблицу ключевых примечаний рядом с исходной и переключает на неё модель.
    /// </summary>
    /// <remarks>
    ///     Исходный файл не трогается: он общий для всех моделей, которые на него ссылаются, и
    ///     перезапись сделала бы русский комплект английским заодно.
    /// </remarks>
    private (ElementTextApplyResult Applied, string Path)? WriteKeynotes(
        Document document,
        CategoryWrite? keynotes,
        Glossary glossary,
        TranslationLayout layout,
        IProgress<string>? progress)
    {
        if (keynotes is null || keynotes.Values.Count is 0)
        {
            return null;
        }

        var file = KeynoteFile.TryRead(document);

        if (file is null)
        {
            return (
                new ElementTextApplyResult
                {
                    Category = Text.CategoryKeynotes,
                    Failures = [Text.EmptyKeynotes]
                },
                string.Empty);
        }

        progress?.Report("Пишу английскую таблицу ключевых примечаний");

        var translations = new Dictionary<string, string>(StringComparer.Ordinal);
        var withoutTranslation = 0;

        foreach (var value in keynotes.Values)
        {
            var translation = Resolve(glossary, value);

            if (translation.Length is 0)
            {
                withoutTranslation++;
                continue;
            }

            translations[value.Text.Target.Key] = layout.Compose(value.Text.SourceText, translation);
        }

        var written = file.WriteTranslated(document, translations, config.Translation.TargetLanguage);

        return (
            new ElementTextApplyResult
            {
                Category = Text.CategoryKeynotes,
                WrittenCount = written.Count,
                UnchangedCount = withoutTranslation
            },
            written.Path);
    }

    /// <summary>
    ///     Ставит транзакции разбор отказов: без него одно предупреждение Ревита откатывает всю
    ///     пачку переводов, и снаружи это выглядит как «кнопка ничего не делает».
    /// </summary>
    private static void Prepare(Transaction transaction)
    {
        var options = transaction.GetFailureHandlingOptions();

        options.SetFailuresPreprocessor(new TranslationFailures());

        transaction.SetFailureHandlingOptions(options);
    }

    /// <summary>Перевод из окна, а если там пусто - из глоссария после машинного прохода.</summary>
    private static string Resolve(Glossary glossary, CategoryValue value)
    {
        var typed = value.Translation.Trim();

        if (typed.Length > 0)
        {
            return typed;
        }

        return glossary.TryFind(value.Text.SourceText, out var found) ? found.Trim() : string.Empty;
    }

    /// <summary>
    ///     Записывает переводы категорий модели: каждая категория одной транзакцией, чтобы сбой на
    ///     одной не оставил половину другой.
    /// </summary>
    /// <param name="withoutTranslation">
    ///     Сколько отмеченных строк осталось без перевода: ни в окне, ни в глоссарии. Без этого счёта
    ///     прогон выглядел бы удачным, ничего не записав.
    /// </param>
    private static IReadOnlyList<ElementTextApplyResult> Apply(
        Document document,
        IReadOnlyList<CategoryWrite> categories,
        Glossary glossary,
        TranslationLayout layout,
        out int withoutTranslation)
    {
        var applied = new List<ElementTextApplyResult>();

        withoutTranslation = 0;

        foreach (var category in categories.Where(category => category.Values.Count > 0))
        {
            var writer = new ElementTextWriter(document);

            using var transaction = new Transaction(document, $"Перевод: {category.Name}");

            Prepare(transaction);

            transaction.Start();

            foreach (var value in category.Values)
            {
                var translation = Resolve(glossary, value);

                if (translation.Length is 0)
                {
                    // Пустой перевод для записи означает откат, а строку никто не переводил -
                    // трогать её нельзя, иначе отмеченное без перевода откатывало бы чужие правки.
                    withoutTranslation++;
                    continue;
                }

                writer.Write(value.Text, layout.Compose(value.Text.SourceText, translation));
            }

            transaction.Commit();

            applied.Add(writer.Describe(category.Name));
        }

        return applied;
    }
}
