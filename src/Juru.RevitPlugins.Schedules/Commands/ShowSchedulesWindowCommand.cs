using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
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

    /// <summary>
    ///     Регистрирует поиск зависимостей рядом с этой сборкой один раз на сеанс - раньше любого
    ///     обращения к ним.
    /// </summary>
    /// <remarks>
    ///     AddinManager копирует всю папку сборки во временный каталог, и нужные DLL (например,
    ///     System.Text.Encoding.CodePages, которым <see cref="Services.ScheduleTextExporter" />
    ///     подключает кодовую страницу 1251) лежат там же, рядом. Но .NET Framework их не находит:
    ///     обычная привязка Fusion не пробует папку загруженной сборки сама по себе - тот же класс
    ///     проблемы, что уже встречался с Wpf.Ui.dll под pyRevit (см. память проекта), только без
    ///     стороннего конфликтующего экземпляра - просто мимо. При обычной установке эта DLL лежит
    ///     в папке плагина и обычно резолвится штатно, но обработчик безвреден и там - сработает,
    ///     только если понадобится.
    /// </remarks>
    static ShowSchedulesWindowCommand()
    {
        AppDomain.CurrentDomain.AssemblyResolve += ResolveSiblingAssembly;
    }

    private static Assembly? ResolveSiblingAssembly(object sender, ResolveEventArgs args)
    {
        var directory = Path.GetDirectoryName(typeof(ShowSchedulesWindowCommand).Assembly.Location);

        if (directory is null)
        {
            return null;
        }

        var path = Path.Combine(directory, new AssemblyName(args.Name).Name + ".dll");

        return File.Exists(path) ? Assembly.LoadFrom(path) : null;
    }

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
        var modelKey = DocumentKey(Application.ActiveUIDocument.Document);
        var host = new Host(modelKey, config, new RevitActionRunner());
        var viewModel = new SchedulesWindowViewModel(config, host);

        host.Attach(viewModel);

        openWindow = new SchedulesWindow(viewModel);

        CloseWithDocument(openWindow, Application.Application, modelKey);

        new WindowInteropHelper(openWindow) { Owner = Application.MainWindowHandle };

        openWindow.Show();

        // Чтение идёт уже под открытым окном: оно занимает секунды, и всё это время Ревит выглядел бы
        // зависшим. Внешнее событие здесь работает именно потому, что окно немодальное.
        host.Load();
    }

    /// <summary>
    ///     Закрывает окно вместе с моделью, для которой оно открыто.
    /// </summary>
    /// <remarks>
    ///     Окно немодальное и живёт само по себе: Ревит закроет файл, а оно останется висеть со
    ///     строками исчезнувшей модели. Дальше любое действие обращается к закрытому документу, а
    ///     это не исключение, а <c>AccessViolationException</c> из нативной части Ревита —
    ///     «Attempted to read or write protected memory». Заодно снимается и вторая беда: экземпляр
    ///     окна один на сеанс, и без закрытия команда в новой модели просто активировала бы старое
    ///     окно, привязанное к закрытой.
    /// </remarks>
    private static void CloseWithDocument(
        System.Windows.Window window,
        Autodesk.Revit.ApplicationServices.Application application,
        string path)
    {
        void OnClosing(object sender, Autodesk.Revit.DB.Events.DocumentClosingEventArgs args)
        {
            if (string.Equals(DocumentKey(args.Document), path, StringComparison.CurrentCultureIgnoreCase))
            {
                window.Close();
            }
        }

        application.DocumentClosing += OnClosing;

        window.Closed += (_, _) =>
        {
            application.DocumentClosing -= OnClosing;
            openWindow = null;
        };
    }

    /// <summary>
    ///     Чем отличать одну открытую модель от другой. Путь, а не сам <c>Document</c>: ссылки на
    ///     него сравнивать нельзя — Ревит отдаёт обёртки, а у несохранённой модели пути нет, и тогда
    ///     остаётся имя.
    /// </summary>
    private static string DocumentKey(Document document)
    {
        return document.PathName is { Length: > 0 } path ? path : document.Title;
    }

    /// <summary>
    ///     Сторона модели: выполняет запросы окна во внешнем событии.
    /// </summary>
    private sealed class Host(string modelKey, SchedulesConfig config, RevitActionRunner runner)
        : ISchedulesHost
    {
        private SchedulesWindowViewModel? viewModel;

        /// <summary>
        ///     Живой <c>UIDocument</c> нашей модели либо <c>null</c>, если её больше нет.
        /// </summary>
        /// <remarks>
        ///     Берётся заново на каждое действие, а не запоминается при открытии окна: запомненный
        ///     переживает закрытие файла, и обращение к нему валит Ревит обращением к освобождённой
        ///     памяти. По той же причине модель опознаётся запомненной строкой пути, а не ссылкой на
        ///     <c>Document</c>: у закрытого и путь уже не спросить. Ищется среди открытых моделей, а
        ///     не берётся активная: активной может быть вторая модель, а работать окно должно со
        ///     своей.
        /// </remarks>
        private UIDocument? Live(UIApplication uiApplication)
        {
            // Активный, если это наша модель: у него есть активный вид, а у собранного по документу
            // его может не быть вовсе.
            if (uiApplication.ActiveUIDocument is { } active
                && active.Document.IsValidObject
                && string.Equals(DocumentKey(active.Document), modelKey, StringComparison.CurrentCultureIgnoreCase))
            {
                return active;
            }

            foreach (Document open in uiApplication.Application.Documents)
            {
                if (open.IsValidObject
                    && !open.IsLinked
                    && string.Equals(DocumentKey(open), modelKey, StringComparison.CurrentCultureIgnoreCase))
                {
                    return new UIDocument(open);
                }
            }

            return null;
        }

        public void Attach(SchedulesWindowViewModel attached)
        {
            viewModel = attached;
        }

        public void Load()
        {
            runner.Run(uiApplication =>
            {
                using var suppression = RevitContext.BeginDialogSuppressionScope(TaskDialogResult.Ok);

                if (Live(uiApplication) is not { } uiDocument)
                {
                    viewModel!.FinishLoading(Text.StatusModelClosed);

                    return;
                }

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
                        BuildCategories(Scope(uiApplication, uiDocument), glossary, () => viewModel!.Layout));

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
                // Своя модель, а не активная: активной может быть вторая, а закрытой - ни одной.
                if (Live(uiApplication) is not { } document)
                {
                    viewModel!.StatusNote = Text.StatusModelClosed;

                    return;
                }

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

                if (Live(uiApplication) is not { } document)
                {
                    viewModel!.StatusNote = Text.StatusModelClosed;

                    return;
                }

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

            runner.Run(uiApplication =>
            {
                if (Live(uiApplication) is not { } uiDocument)
                {
                    viewModel!.SetBusy(false, Text.StatusModelClosed);

                    return;
                }

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

                viewModel!.SetBusy(false, Describe(result) + DescribeReport(result));

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
        private CollectScope Scope(UIApplication uiApplication, UIDocument uiDocument)
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

        /// <summary>
        ///     Итог прогона одной строкой: что нарисовано, что записано в модель, что не далось.
        /// </summary>
        /// <remarks>
        ///     Строка состояния - единственное место, где итог виден всегда: окно с подробностями
        ///     показывается только при сбоях. Поэтому здесь все числа, а не «Готово», и собирается
        ///     она из переводимых строк - интерфейс бывает и английским.
        /// </remarks>
        private static string Describe(EnglishSetResult result)
        {
            var parts = new List<string> { Text.NoteDone };

            if (result.DrawnCount + result.FailedCount > 0)
            {
                var schedules = string.Format(Text.NoteSchedulesDrawn, result.DrawnCount);

                parts.Add(result.FailedCount is 0
                    ? schedules + "."
                    : $"{schedules}, {string.Format(Text.NoteSchedulesFailed, result.FailedCount)}.");
            }

            parts.Add(string.Format(
                Text.NoteWritten,
                result.Categories.Sum(category => category.WrittenCount),
                result.Categories.Sum(category => category.RevertedCount)));

            if (result.TranslatedNowCount > 0)
            {
                parts.Add(string.Format(Text.NoteMachineTranslated, result.TranslatedNowCount));
            }

            if (result.WithoutTranslationCount > 0)
            {
                parts.Add(string.Format(Text.NoteSkippedWithoutTranslation, result.WithoutTranslationCount));
            }

            return string.Join(" ", parts);
        }

        /// <summary>
        ///     Путь отчёта для строки состояния: без диалога его иначе негде взять.
        /// </summary>
        private static string DescribeReport(EnglishSetResult result)
        {
            return result.ReportPath is null ? string.Empty : $" {Text.NoteReportAt} {result.ReportPath}";
        }

        /// <summary>
        ///     Показывает итог прогона: что удалось и что нет.
        /// </summary>
        /// <remarks>
        ///     Разделено на «сделано» и «не получилось»: в одном списке вперемешку числа успеха
        ///     теряются среди причин отказа, а решение принимают как раз по вторым. Строки, которые
        ///     не к чему сказать, в текст не попадают - пустая «Ошибок перевода: 0» только мешает.
        /// </remarks>
        private static void ShowResult(EnglishSetResult result)
        {
            var done = new List<string>
            {
                string.Format(Text.DialogTranslationService, result.TranslatorNote, result.TranslatedNowCount)
            };

            if (result.DrawnCount + result.FailedCount > 0)
            {
                done.Add(string.Format(Text.DialogSchedules, result.DrawnCount, result.FailedCount));
            }

            done.Add(DescribeCategories(result));

            if (result.KeynotePath is { Length: > 0 } keynotes)
            {
                done.Add(string.Format(Text.DialogKeynotes, keynotes));
            }

            var problems = new List<string>();

            if (result.UntranslatedCount > 0)
            {
                problems.Add(string.Format(Text.DialogDictionaryUntranslated, result.UntranslatedCount));
            }

            if (result.WithoutTranslationCount > 0)
            {
                problems.Add(string.Format(Text.DialogSkipped, result.WithoutTranslationCount));
            }

            if (result.Failures.Count > 0)
            {
                problems.Add(string.Format(Text.DialogFailures, result.Failures.Count, result.Failures[0]));
            }

            foreach (var failure in result.Categories.SelectMany(category => category.Failures))
            {
                problems.Add(failure);
            }

            var dialog = new TaskDialog(DialogTitle)
            {
                MainInstruction = Describe(result),
                MainContent = string.Join(Environment.NewLine, done),
                ExpandedContent = problems.Count is 0
                    ? string.Empty
                    : string.Join(Environment.NewLine, problems),
                CommonButtons = TaskDialogCommonButtons.Close
            };

            if (result.ReportPath is not null)
            {
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, Text.DialogOpenReport);
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
                return Text.DialogNoCategories;
            }

            return string.Join(
                Environment.NewLine,
                result.Categories.Select(category => string.Format(
                    Text.DialogCategoryLine,
                    category.Category,
                    category.WrittenCount,
                    category.RevertedCount,
                    category.UnchangedCount)));
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
