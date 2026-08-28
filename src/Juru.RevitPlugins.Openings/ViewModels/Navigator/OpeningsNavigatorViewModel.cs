using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
// Псевдонимы: в родительском пространстве имён лежит свой RelayCommand, оставшийся от старых окон.
using ShowTaskCommand = CommunityToolkit.Mvvm.Input.RelayCommand<
    Juru.RevitPlugins.Openings.ViewModels.Navigator.OpeningTaskViewModel>;
using ToolkitCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

using Juru.RevitPlugins.Core.FileNaming;
using Juru.RevitPlugins.Openings.Models.Configs;
using Juru.RevitPlugins.Openings.Models.Intersections;
using Juru.RevitPlugins.Openings.Models.Navigator;
using Juru.RevitPlugins.Openings.Models.Server;
using Juru.RevitPlugins.Openings.Models.Server.Dto;
using Juru.RevitPlugins.Openings.Services;

namespace Juru.RevitPlugins.Openings.ViewModels.Navigator;

/// <summary>
/// Навигатор по заданиям на отверстия, выданным из активного файла.
/// </summary>
public partial class OpeningsNavigatorViewModel : ObservableObject {
    private readonly OpeningsConfig _config;
    private readonly RevitActionRunner _runner;
    private readonly RevitFileNameInfo? _fileInfo;
    private readonly OpeningsServerClient? _client;
    private View3D? _view;
    private HostReference? _highlightedHost;

    [ObservableProperty]
    private OpeningTaskViewModel? _selectedTask;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private string _linksText = string.Empty;

    [ObservableProperty]
    private bool _noLinksLoaded;

    [ObservableProperty]
    private string _serverText = string.Empty;

    [ObservableProperty]
    private bool _decisionsUnknown;

    /// <param name="fileInfo">
    /// Разобранное имя файла - им адресуются задания на сервере. <c>null</c>, если имя не по
    /// стандарту: навигатор тогда работает без решений, состояния считаются как и раньше.
    /// </param>
    /// <param name="client">Клиент сервера согласования либо <c>null</c>, если адрес не задан.</param>
    public OpeningsNavigatorViewModel(
        OpeningsConfig config,
        RevitActionRunner runner,
        RevitFileNameInfo? fileInfo = null,
        OpeningsServerClient? client = null) {

        _config = config ?? throw new ArgumentNullException(nameof(config));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _fileInfo = fileInfo;
        _client = client;

        Tasks = [];
        // ShowCommand обслуживает только двойной клик по строке DataGrid - там CommandParameter
        // не привязан внутри ячейки, и известная ловушка с CanExecute не проявляется. Кнопки
        // "Показать"/"Хост" в шаблонных колонках вызываются напрямую из code-behind по Click, см. окно.
        ShowCommand = new ShowTaskCommand(Show, task => task is not null);
        RefreshCommand = new ToolkitCommand(Refresh);
    }

    /// <summary>
    /// Задания активного файла.
    /// </summary>
    public ObservableCollection<OpeningTaskViewModel> Tasks { get; }

    /// <summary>
    /// Название набора настроек, по которым назначены состояния.
    /// </summary>
    public string ConfigName => _config.Name;

    /// <summary>
    /// Выделяет задание в модели и показывает его на виде.
    /// </summary>
    public IRelayCommand<OpeningTaskViewModel> ShowCommand { get; }

    /// <summary>
    /// Пересчитывает состояния заново.
    /// </summary>
    public IRelayCommand RefreshCommand { get; }

    /// <summary>
    /// Перечитывает задания и пересчитывает их состояния. Вызывается только в контексте Revit API.
    /// </summary>
    public void Reload(UIApplication uiApplication) {
        if(uiApplication is null) { throw new ArgumentNullException(nameof(uiApplication)); }

        var doc = uiApplication.ActiveUIDocument.Document;
        var tasks = new OpeningTasksProvider(doc, _config).GetTasks();
        var links = new StructureLinksProvider(doc, _config).GetProviders();

        NoLinksLoaded = links.Count == 0;
        LinksText = NoLinksLoaded
            ? "Связей с конструкциями не найдено: состояния определены только по инженерным элементам, "
                + "проверка на недопустимые/разные конструкции не работает. Загрузите связи АР и КР."
            : "Связи с конструкциями: "
                + string.Join(", ", links.Select(link => $"{link.LinkName} ({link.Discipline})"));

        // Без связей проверка статусов бессмысленна почти целиком (недопустимые/разные конструкции,
        // а часто и хост вообще не находится) - тихой строки подсказки недостаточно, реально
        // сбивает с толку, когда все задания выглядят как никогда не проверявшиеся.
        if(NoLinksLoaded && tasks.Count > 0) {
            TaskDialog.Show("Задания на отверстия",
                "Не найдено ни одной связи с конструкциями (АР, КР). Состояния заданий без связей "
                + "не могут быть посчитаны правильно - загрузите связи и обновите навигатор.");
        }

        var updated = UpdateStatuses(uiApplication, doc, tasks, links);
        var decisions = GetDecisions(out bool serverChecked);

        // Отдельный вид с постоянной раскраской: зелёные инженерные системы, красные задания,
        // серые конструкции из связей, притушенное остальное - как у Bim4Everyone. Настраивается
        // при каждом обновлении, а не только при первом открытии: набор категорий зависит
        // от настроек, которые пользователь может поменять между обновлениями.
        _view = NavigatorViewProvider.GetOrCreate(doc, "Исходящие");
        _highlightedHost = null;
        NavigatorColorScheme.ApplyOutgoing(doc, _view, GetMepCategories(), GetStructureCategories());

        Tasks.Clear();
        foreach(var task in updated) {
            decisions.TryGetValue(task.UniqueId, out var decision);
            Tasks.Add(new OpeningTaskViewModel(task, decision, serverChecked));
        }

        Summary = GetSummary(Tasks, serverChecked);
        SelectedTask = Tasks.FirstOrDefault();
    }

    /// <summary>
    /// Забирает с сервера решения по заданиям своей дисциплины и раскладывает их по ключу задания -
    /// <c>UniqueId</c> экземпляра, тот же, под которым задание уходило на сервер.
    /// </summary>
    /// <remarks>
    /// Недоступный сервер навигатор не останавливает: техническое состояние считается по геометрии
    /// и без сети, ради него окно и открывают. Поэтому ошибка становится строкой в шапке, а колонки
    /// решений показывают "неизвестно", а не пустоту, которую можно принять за "никто не ответил".
    /// </remarks>
    private IReadOnlyDictionary<string, OpeningTaskViewDto> GetDecisions(out bool serverChecked) {
        serverChecked = false;
        DecisionsUnknown = true;

        if(_fileInfo is null) {
            ServerText = "Решения согласующих не показаны: имя файла не по стандарту "
                + "(Проект-Объект-Дисциплина-vNN), и на сервере не найти, к какому объекту относятся задания.";
            return new Dictionary<string, OpeningTaskViewDto>();
        }

        if(_client is null) {
            ServerText = "Решения согласующих не показаны: адрес сервера не задан "
                + "ни в настройках плагина, ни в ClashServer.json рядом со сборкой.";
            return new Dictionary<string, OpeningTaskViewDto>();
        }

        OpeningTaskViewDto[] serverTasks;
        try {
            serverTasks = Task.Run(() => _client.GetOpeningsAsync(
                    _fileInfo.ProjectCode, _fileInfo.ObjectName, [_fileInfo.Discipline]))
                .GetAwaiter().GetResult();
        } catch(Exception exception) when(exception is InvalidOperationException or TimeoutException) {
            ServerText = "Решения согласующих не загружены: " + exception.Message;
            return new Dictionary<string, OpeningTaskViewDto>();
        }

        serverChecked = true;
        DecisionsUnknown = false;
        ServerText = $"Решения с сервера: {_client.BaseUrl}. Заданий этой дисциплины на сервере: {serverTasks.Length}.";

        var byKey = new Dictionary<string, OpeningTaskViewDto>();
        foreach(var task in serverTasks) {
            if(!string.IsNullOrWhiteSpace(task.TaskKey)) {
                byKey[task.TaskKey] = task;
            }
        }

        return byKey;
    }

    private IReadOnlyCollection<OpeningTask> UpdateStatuses(
        UIApplication uiApplication,
        Document doc,
        IReadOnlyCollection<OpeningTask> tasks,
        IReadOnlyCollection<StructureElementsProvider> links) {

        if(tasks.Count == 0) {
            return tasks;
        }

        var mepElements = new MepElementsIndex(doc, _config);
        var updater = new OpeningTaskStatusUpdater(doc, _config, mepElements, links, tasks);

        using var progress = new OperationProgress(uiApplication.MainWindowHandle);
        progress.StartStage("Проверка заданий", tasks.Count);

        List<OpeningTask> processed = [];
        int done = 0;
        foreach(var task in tasks) {
            if(progress.Token.IsCancellationRequested) {
                // Отменённая проверка не оставляет список пустым: показываем то, что успели посчитать.
                break;
            }

            updater.Update(task);
            processed.Add(task);
            progress.Report(++done);
        }

        return processed;
    }

    /// <summary>
    /// Возвращает сводку: сколько заданий требуют внимания по геометрии и как идёт согласование.
    /// </summary>
    private static string GetSummary(IReadOnlyCollection<OpeningTaskViewModel> tasks, bool serverChecked) {
        if(tasks.Count == 0) {
            return "Заданий на отверстия в файле нет.";
        }

        int attention = tasks.Count(task => task.NeedsAttention);
        string summary = $"Заданий: {tasks.Count}. Требуют внимания: {attention}.";
        if(!serverChecked) {
            return summary;
        }

        int approved = tasks.Count(task => task.IsApproved);
        int rejected = tasks.Count(task => task.IsRejected);
        int notSent = tasks.Count(task => task.Decision is null);
        summary += $" Согласовано обеими: {approved}, отклонено: {rejected}.";
        if(notSent > 0) {
            summary += $" Не отправлялось на сервер: {notSent}.";
        }

        return summary;
    }

    /// <summary>
    /// Выделяет задание и зумит точно на него - как "Выбрать" у Bim4Everyone, а не просто
    /// <c>ShowElements</c>: тот подгоняет вид под всё выделение сразу, и на плотной модели
    /// не всегда понятно, куда именно пришла навигация. Хост при этом подсвечивается для
    /// контекста, но не входит в зум - контекст важен, а не то, что видно ровно вместе с ним.
    /// </summary>
    public void Show(OpeningTaskViewModel? task) {
        if(task is null) {
            return;
        }

        _runner.Run(uiApplication => {
            var uiDocument = uiApplication.ActiveUIDocument;
            var doc = uiDocument.Document;
            var view = GetView(doc);
            uiDocument.ActiveView = view;

            var next = GetHostReference(doc, task.Task.Host);
            HostHighlighter.Apply(doc, view, _highlightedHost, next);
            _highlightedHost = next;

            ZoomTo(uiDocument, view, task.Task.Instance, link: null);
        });
    }

    /// <summary>
    /// Выделяет и показывает хост задания - стену или перекрытие, которое оно пробивает,
    /// вместо самого задания. Вторая кнопка ("лупа"), как у Bim4Everyone: одна кнопка ведёт
    /// к заданию, другая - к его основе.
    /// </summary>
    public void ShowHost(OpeningTaskViewModel? task) {
        var host = task?.Task.Host;
        if(host is null) {
            return;
        }

        _runner.Run(uiApplication => {
            var uiDocument = uiApplication.ActiveUIDocument;
            var doc = uiDocument.Document;
            var view = GetView(doc);
            uiDocument.ActiveView = view;

            var next = GetHostReference(doc, host);
            HostHighlighter.Apply(doc, view, _highlightedHost, next);
            _highlightedHost = next;

            ZoomTo(uiDocument, view, host, next?.Link);
        });
    }

    /// <summary>
    /// Вид навигатора мог не настроиться, если это первый показ до какого-либо обновления -
    /// тогда создаём его прямо тут же, без раскраски: она настроится при следующем Reload.
    /// </summary>
    private View3D GetView(Document doc) {
        return _view ??= NavigatorViewProvider.GetOrCreate(doc, "Исходящие");
    }

    /// <summary>
    /// Выделяет элемент и подгоняет вид под его собственный габарит - и для задания в активном
    /// документе, и для хоста из связи: выделение в связи требует
    /// <see cref="Reference.CreateLinkReference"/>, обычный <c>ElementId</c> с ней не работает.
    /// </summary>
    /// <remarks>
    /// Раньше задание показывалось простым <c>ShowElements</c> без точного зума - тем самым багом,
    /// с которым уже боролись: "непонятно, куда пришла навигация". Зум должен быть одинаковым
    /// для обоих кликов, различие только в системе координат габарита.
    /// </remarks>
    private static void ZoomTo(UIDocument uiDocument, View3D view, Element target, RevitLinkInstance? link) {
        var box = target.get_BoundingBox(null);
        if(box is not null) {
            var transform = link?.GetTotalTransform() ?? Transform.Identity;
            var first = transform.OfPoint(box.Min);
            var second = transform.OfPoint(box.Max);
            double margin = UnitUtils.ConvertToInternalUnits(500, UnitTypeId.Millimeters);

            // SetSectionBox/IsSectionBoxActive меняют документ - без транзакции Revit падает
            // с "Attempt to modify the model outside of transaction".
            using var transaction = new Transaction(uiDocument.Document, "Зум навигатора заданий на отверстия");
            transaction.Start();
            view.SetSectionBox(new BoundingBoxXYZ {
                Min = new XYZ(
                    Math.Min(first.X, second.X) - margin,
                    Math.Min(first.Y, second.Y) - margin,
                    Math.Min(first.Z, second.Z) - margin),
                Max = new XYZ(
                    Math.Max(first.X, second.X) + margin,
                    Math.Max(first.Y, second.Y) + margin,
                    Math.Max(first.Z, second.Z) + margin)
            });
            view.IsSectionBoxActive = true;
            transaction.Commit();
        }

        var uiView = uiDocument.GetOpenUIViews().FirstOrDefault(openView => openView.ViewId == view.Id);
        uiView?.ZoomToFit();

        if(link is null) {
            uiDocument.Selection.SetElementIds(new List<ElementId> { target.Id });
        } else {
            uiDocument.Selection.SetReferences(new List<Reference> { new Reference(target).CreateLinkReference(link) });
        }

        // Без этого рамка выделения иногда не перерисовывается сразу после программного
        // SetElementIds/SetReferences - видно только после того, как пользователь сам кликнет в вид.
        uiDocument.RefreshActiveView();
    }

    /// <summary>
    /// Находит связь, из которой взят хост, чтобы подсветить его переопределением графики
    /// именно в этой связи - хост лежит в документе связи, а не в активном документе.
    /// </summary>
    private static HostReference? GetHostReference(Document doc, Element? host) {
        if(host is null) {
            return null;
        }

        var link = new FilteredElementCollector(doc)
            .OfClass(typeof(RevitLinkInstance))
            .Cast<RevitLinkInstance>()
            .FirstOrDefault(instance => instance.GetLinkDocument()?.Equals(host.Document) == true);
        return link is null ? null : new HostReference(host.Id, link);
    }

    private IReadOnlyCollection<BuiltInCategory> GetMepCategories() {
        return _config.GetSelectedCategories()
            .SelectMany(category => category.Kind.GetCurveCategories().Concat(category.Kind.GetFittingCategories()))
            .Distinct()
            .ToArray();
    }

    private IReadOnlyCollection<BuiltInCategory> GetStructureCategories() {
        return _config.GetSelectedCategories()
            .SelectMany(category => category.Structures)
            .Distinct()
            .Select(kind => kind.GetCategory())
            .ToArray();
    }

    private void Refresh() {
        _runner.Run(Reload);
    }
}
