using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Juru.RevitPlugins.Schedules.Localization;
using Juru.RevitPlugins.Schedules.Models;
using Juru.RevitPlugins.Schedules.Services;

using Nice3point.Revit.Toolkit;
using Nice3point.Revit.Toolkit.External;

namespace Juru.RevitPlugins.Schedules.Commands;

/// <summary>
///     Пересобирает все переведённые копии модели по их оригиналам: оригинал изменился — копия догоняет.
/// </summary>
/// <remarks>
///     <para>
///         Копии ищутся по всей модели, и открытый вид на это не влияет: кнопка делает одно и то же,
///         что бы ни было открыто. Зависимость от активного вида превратила бы одну кнопку в три
///         команды, о которых пришлось бы помнить, - а копий в модели единицы, и пересобрать их все
///         дешевле, чем выбирать.
///     </para>
///     <para>
///         Ни окна, ни полосы прогресса: служба перевода не зовётся, глоссарий не пополняется — копия
///         собирается по тому, что в нём уже есть.
///     </para>
///     <para>
///         Пересобирается копия целиком: что именно изменилось в оригинале, узнать неоткуда - Ревит
///         не хранит ни версии таблицы, ни времени её правки, а сравнивать напечатанный текст с
///         переведённым нельзя, это разные языки.
///     </para>
/// </remarks>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public sealed class UpdateScheduleCopyCommand : ExternalCommand
{
    public override void Execute()
    {
        var uiDocument = Application.ActiveUIDocument;
        var document = uiDocument.Document;

        var config = SchedulesConfig.Load();
        var copies = ScheduleCopyLink.FindAllCopies(document);

        var originals = new List<ViewSchedule>();
        var orphans = new List<string>();

        foreach (var copy in copies)
        {
            if (ScheduleCopyLink.TryFindOriginal(document, copy) is { } original)
            {
                originals.Add(original);
                continue;
            }

            // Оригинал удалили: копия остаётся как есть. Удалять её за пользователя нельзя - это
            // единственное, что от той таблицы осталось.
            orphans.Add(copy.Name);
        }

        originals.AddRange(Unlinked(document, config.Translation.TargetLanguage));

        var targets = originals.GroupBy(original => original.Id).Select(group => group.First()).ToList();

        if (targets.Count is 0 && orphans.Count is 0)
        {
            TaskDialog.Show(Text.UpdateTitle, Text.UpdateNoCopies);

            return;
        }

        var updated = Rebuild(uiDocument, config, targets);

        TaskDialog.Show(Text.UpdateTitle, Describe(updated, orphans));
    }

    /// <summary>
    ///     Оригиналы копий, в которых связи нет: копия сделана сборкой, которая её ещё не записывала.
    ///     Такие копии узнаются по имени - тому самому, какое копия получила бы сейчас, - и после
    ///     пересборки связь у них появляется.
    /// </summary>
    private static IReadOnlyList<ViewSchedule> Unlinked(Document document, string language)
    {
        var schedules = new FilteredElementCollector(document)
            .OfClass(typeof(ViewSchedule))
            .Cast<ViewSchedule>()
            .Where(view => !view.IsTemplate)
            .ToList();

        var names = schedules.Select(view => view.Name).ToHashSet(StringComparer.CurrentCultureIgnoreCase);

        return schedules
            .Where(view => ScheduleCopyLink.TryReadOriginalId(view) is null)
            .Where(view => names.Contains(ScheduleCopyName.For(view.Name, language)))
            .Where(view => !string.Equals(
                view.Name,
                ScheduleCopyName.For(view.Name, language),
                StringComparison.CurrentCultureIgnoreCase))
            .ToList();
    }

    private static IReadOnlyList<SheetScheduleCopy> Rebuild(
        UIDocument uiDocument,
        SchedulesConfig config,
        IReadOnlyList<ViewSchedule> originals)
    {
        if (originals.Count is 0)
        {
            return [];
        }

        var layout = new TranslationLayout
        {
            KeepSource = config.Translation.KeepSourceText,
            Separator = config.Translation.Separator
        };

        var glossary = Glossary.Load(config.ResolveGlossaryPath(), layout);

        // Проверка значений параметров показывает модальное окно, которое обрывает чтение на середине.
        using var suppression = RevitContext.BeginDialogSuppressionScope(TaskDialogResult.Ok);

        var service = new SheetScheduleCopyService(uiDocument);

        var read = service.ReadPrinted(
            originals.Select(original => new SheetSchedule(original, [])).ToList(),
            null);

        return service.CopyAllAsSchedules(read, glossary, config.Translation.TargetLanguage);
    }

    private static string Describe(IReadOnlyList<SheetScheduleCopy> updated, IReadOnlyList<string> orphans)
    {
        var lines = updated
            .Select(copy => copy.Error is { Length: > 0 } error
                ? string.Format(Text.UpdateFailedLine, copy.ScheduleName, error)
                : string.Format(Text.UpdateDoneLine, copy.ViewName, copy.RowCount))
            .Concat(orphans.Select(name => string.Format(Text.UpdateOrphanLine, name)));

        return string.Join("\n", lines);
    }
}
