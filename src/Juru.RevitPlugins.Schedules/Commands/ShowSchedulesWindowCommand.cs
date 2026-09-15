using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Interop;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Juru.RevitPlugins.Core.Theming;

using Juru.RevitPlugins.Schedules.Models;
using Juru.RevitPlugins.Schedules.Services;
using Juru.RevitPlugins.Schedules.Services.Categories;
using Juru.RevitPlugins.Schedules.ViewModels;
using Juru.RevitPlugins.Schedules.Views;

using Nice3point.Revit.Toolkit;
using Nice3point.Revit.Toolkit.External;

using Juru.RevitPlugins.Schedules.Localization;

namespace Juru.RevitPlugins.Schedules.Commands;

/// <summary>
///     Показывает выбор спецификаций для английского комплекта.
/// </summary>
/// <remarks>
///     Окно немодальное, как навигаторы: из него открывают спецификации и элементы модели, а под
///     модальным окном ни сменить активный вид, ни выполнить внешнее событие нельзя. Поэтому всё,
///     что трогает модель, уходит через <see cref="RevitActionRunner" />. Экземпляр один на сеанс:
///     второй показывал бы состояние, посчитанное в другой момент.
/// </remarks>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public sealed class ShowSchedulesWindowCommand : ExternalCommand
{
    private static string DialogTitle
    {
        get { return Text.WindowTitle; }
    }
    private const string RoomsCategory = "Помещения";

    private static SchedulesWindow? openWindow;

    public override void Execute()
    {
        if (openWindow is not null)
        {
            openWindow.Activate();
            return;
        }

        // Язык ставится до создания окон: разметка читает строки при разборе XAML.
        Text.Use(Application.Application.Language);

        JuruTheme.Apply();

        var config = SchedulesConfig.Load();
        var host = new Host(Application.ActiveUIDocument, config, new RevitActionRunner());
        var viewModel = new SchedulesWindowViewModel(config, host);

        host.Attach(viewModel);

        openWindow = new SchedulesWindow(viewModel);
        openWindow.Closed += (_, _) => openWindow = null;

        new WindowInteropHelper(openWindow) { Owner = Application.MainWindowHandle };

        openWindow.Show();

        // Чтение идёт уже под открытым окном: оно занимает секунды, и всё это время Ревит выглядел бы
        // зависшим. Внешнее событие здесь работает именно потому, что окно немодальное.
        host.Load();
    }

    /// <summary>
    ///     Сторона модели: выполняет запросы окна во внешнем событии.
    /// </summary>
    private sealed class Host(UIDocument uiDocument, SchedulesConfig config, RevitActionRunner runner)
        : ISchedulesHost
    {
        private SchedulesWindowViewModel? viewModel;

        public void Attach(SchedulesWindowViewModel attached)
        {
            viewModel = attached;
        }

        public void Load()
        {
            runner.Run(uiApplication =>
            {
                using var suppression = RevitContext.BeginDialogSuppressionScope(TaskDialogResult.Ok);

                try
                {
                    var glossary = GlossaryFile.Read(config.ResolveGlossaryPath());
                    var service = new SheetScheduleCopyService(uiDocument);
                    var sheetId = ScheduleSheetScope(uiApplication);

                    viewModel!.StartReading(service.CountOnSheets(sheetId));

                    var schedules = service.ReadForSelection(
                        new DirectProgress(name => viewModel!.Reading(name)), sheetId);

                    viewModel!.Fill(
                        schedules.Select(schedule => new ScheduleRowViewModel(schedule, glossary)).ToList(),
                        TranslationRowViewModel.Build(schedules, glossary, () => viewModel!.Layout),
                        BuildCategories(Scope(uiApplication), glossary, () => viewModel!.Layout));

                    if (schedules.Count is 0)
                    {
                        viewModel!.StatusNote = Text.StatusNoSchedules;
                    }
                }
                catch (Exception)
                {
                    // Иначе окно навсегда осталось бы в состоянии загрузки: строка «читаю» на месте,
                    // а «Построить» серая без объяснения.
                    viewModel!.FinishLoading(Text.StatusReadFailed);

                    throw;
                }
            });
        }

        /// <summary>
        ///     Открывает вид спецификации. Спецификация сама себе вид, поэтому её достаточно сделать
        ///     активной — как навигатор делает с видом, в котором показывает задание.
        /// </summary>
        public void ShowView(ElementId viewId)
        {
            runner.Run(uiApplication =>
            {
                var document = uiApplication.ActiveUIDocument;

                if (document.Document.GetElement(viewId) is View view)
                {
                    Activate(document, view);
                }
            });
        }

        /// <summary>
        ///     Делает вид активным. Шаблоны видов активными не делаются — Ревит это запрещает,
        ///     и попытка кончилась бы исключением вместо понятного сообщения.
        /// </summary>
        private static void Activate(UIDocument document, View view)
        {
            if (view.IsTemplate)
            {
                return;
            }

            document.ActiveView = view;
            document.RefreshActiveView();
        }

        /// <summary>
        ///     Открывает вид с элементом и выделяет его. Вид подбирает сам Ревит: помещение видно
        ///     на плане своего уровня, и своего вида под это заводить не нужно.
        /// </summary>
        public void ShowElement(ElementText text)
        {
            runner.Run(uiApplication =>
            {
                using var suppression = RevitContext.BeginDialogSuppressionScope(TaskDialogResult.Ok);

                var document = uiApplication.ActiveUIDocument;

                // Лист, вид и спецификация - сами виды: их открывают, а не ищут в текущем виде.
                // ShowElements для них подходящего вида не находит и молча ничего не делает -
                // именно поэтому «Показать» на вкладках «Листы» и «Виды» не работала.
                if (document.Document.GetElement(text.ElementId) is View view)
                {
                    Activate(document, view);
                    return;
                }

                document.ShowElements(text.ElementId);
                document.Selection.SetElementIds([text.ElementId]);

                // Без этого рамка выделения иногда не перерисовывается сразу после программного
                // SetElementIds - видно только после того, как пользователь сам кликнет в вид.
                document.RefreshActiveView();
            });
        }

        public void Build()
        {
            var translations = viewModel!.GlossaryEntries;
            var selected = viewModel!.SelectedRows.Select(row => row.Read).ToList();
            var categories = viewModel!.SelectedCategories;
            var layout = viewModel!.Layout;
            var redrawAsLines = viewModel!.RedrawAsLines;

            viewModel!.SetBusy(true, Text.StatusBuilding);

            runner.Run(_ =>
            {
                EnglishSetResult result;

                // Подавление диалогов глушит и наш собственный отчёт, поэтому оно закрывается до
                // его показа: иначе прогон выглядит так, будто по кнопке ничего не произошло.
                try
                {
                    using var suppression = RevitContext.BeginDialogSuppressionScope(TaskDialogResult.Ok);

                    result = new EnglishSetBuilder(uiDocument, config).Build(
                        translations,
                        selected,
                        categories,
                        layout,
                        redrawAsLines,
                        new DirectProgress(note => viewModel!.SetBusy(true, note)));
                }
                catch (Exception)
                {
                    viewModel!.SetBusy(false, Text.StatusBuildFailed);

                    throw;
                }

                viewModel!.SetBusy(false, Describe(result));

                ShowResult(result);

                // После записи модель уже другая: имена переведены, а «Показать» по старым
                // строкам ищет то, чего в этом виде больше нет. Поэтому окно перечитывается.
                Load();
            });
        }

        /// <summary>
        ///     Собирает строки всех категорий модели. Сбой в одной категории не должен уронить
        ///     остальные: у каждой свои элементы и свои поводы для ошибок.
        /// </summary>
        /// <summary>
        ///     Область сканирования: вся модель либо активный вид. Вид берётся у приложения в момент
        ///     чтения, а не запоминается: пользователь мог сменить его, пока окно открыто.
        /// </summary>
        private CollectScope Scope(UIApplication uiApplication)
        {
            if (!viewModel!.ScanActiveViewOnly)
            {
                return new CollectScope(uiDocument.Document, null);
            }

            var view = uiApplication.ActiveUIDocument?.ActiveView;

            return new CollectScope(uiDocument.Document, view?.Id);
        }

        /// <summary>
        ///     Лист для сужения списка спецификаций. Спецификации размещены на листах, а не в
        ///     произвольном виде, поэтому «активный вид» имеет смысл, только когда он и есть лист -
        ///     иначе список закономерно пуст: на плане или разрезе спецификаций нет.
        /// </summary>
        private ElementId? ScheduleSheetScope(UIApplication uiApplication)
        {
            if (!viewModel!.ScanActiveViewOnly)
            {
                return null;
            }

            return uiApplication.ActiveUIDocument?.ActiveView is ViewSheet sheet
                ? sheet.Id
                : ElementId.InvalidElementId;
        }

        private static IReadOnlyDictionary<string, IReadOnlyList<ElementTextRowViewModel>> BuildCategories(
            CollectScope scope,
            IReadOnlyDictionary<string, string> glossary,
            Func<TranslationLayout> layout)
        {
            var collected = new Dictionary<string, IReadOnlyList<ElementTextRowViewModel>>(StringComparer.Ordinal);

            foreach (var category in CategoryRegistry.All)
            {
                try
                {
                    collected[category.Key] = category
                        .Collect(scope)
                        .Select(text => new ElementTextRowViewModel(text, glossary, layout))
                        .ToList();
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    collected[category.Key] = [];
                }
            }

            return collected;
        }

        private static string Describe(EnglishSetResult result)
        {
            var written = result.Categories.Sum(category => category.WrittenCount);
            var reverted = result.Categories.Sum(category => category.RevertedCount);

            var schedules = result.DrawnCount + result.FailedCount is 0
                ? string.Empty
                : $"Спецификаций {result.DrawnCount}"
                    + (result.FailedCount is 0 ? string.Empty : $", с ошибками {result.FailedCount}")
                    + ". ";

            var skipped = result.WithoutTranslationCount is 0
                ? string.Empty
                : $" Отмечено без перевода и пропущено: {result.WithoutTranslationCount}.";

            return $"Готово. {schedules}В модели записано {written}, откачено {reverted}.{skipped}";
        }

        private static void ShowResult(EnglishSetResult result)
        {
            var failures = result.Failures.Count is 0
                ? string.Empty
                : $"Ошибок перевода: {result.Failures.Count}. {result.Failures[0]}";

            var skipped = result.WithoutTranslationCount is 0
                ? string.Empty
                : $"{Environment.NewLine}Отмечено, но перевода нет ни в окне, ни в глоссарии: "
                    + $"{result.WithoutTranslationCount}. Такие строки пропущены — переведите их "
                    + "кнопкой «Перевести недостающие» на вкладке категории или впишите вручную.";

            var dialog = new TaskDialog(DialogTitle)
            {
                MainInstruction = Describe(result),
                MainContent = $"""
                               Служба перевода: {result.TranslatorNote}, переведено сейчас {result.TranslatedNowCount}
                               Строк глоссария без перевода: {result.UntranslatedCount}
                               {DescribeCategories(result)}{skipped}
                               {failures}
                               """,
                CommonButtons = TaskDialogCommonButtons.Close
            };

            if (result.ReportPath is not null)
            {
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Открыть отчёт");
            }

            if (dialog.Show() is TaskDialogResult.CommandLink1 && result.ReportPath is not null)
            {
                Process.Start("explorer.exe", $"\"{result.ReportPath}\"");
            }
        }

        private static string DescribeCategories(EnglishSetResult result)
        {
            if (result.Categories.Count is 0)
            {
                return "Категории модели не переводились.";
            }

            return string.Join(
                Environment.NewLine,
                result.Categories.Select(category =>
                    $"{category.Category}: записано {category.WrittenCount}, "
                    + $"откачено {category.RevertedCount}, без изменений {category.UnchangedCount}"));
        }
    }

    /// <summary>
    ///     Сообщает о ходе работы сразу, а не через очередь диспетчера.
    /// </summary>
    /// <remarks>
    ///     <see cref="Progress{T}" /> кладёт вызов в очередь, а очередь окна во время внешнего события
    ///     не прокручивается — все сообщения пришли бы разом в конце.
    /// </remarks>
    private sealed class DirectProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value)
        {
            report(value);
        }
    }
}
