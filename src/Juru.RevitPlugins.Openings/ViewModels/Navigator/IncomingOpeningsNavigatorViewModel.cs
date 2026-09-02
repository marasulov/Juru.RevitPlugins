using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ToolkitCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

using Juru.RevitPlugins.Core.FileNaming;
using Juru.RevitPlugins.Openings.Models.Configs;
using Juru.RevitPlugins.Openings.Models.Navigator;
using Juru.RevitPlugins.Openings.Models.Server;
using Juru.RevitPlugins.Openings.Models.Server.Dto;
using Juru.RevitPlugins.Openings.Services;

namespace Juru.RevitPlugins.Openings.ViewModels.Navigator;

/// <summary>
/// Навигатор согласования: задания на отверстия, полученные с сервера, в файле АР или КР.
/// </summary>
/// <remarks>
/// Решения копятся в окне и уходят одной пачкой по кнопке "Сохранить" - так же, как согласование
/// коллизий, потому что HTTP-вызов идёт в потоке интерфейса. См. "POST api/openings/status"
/// в <c>docs/OpeningsApi.md</c>.
/// </remarks>
public partial class IncomingOpeningsNavigatorViewModel : ObservableObject {
    private readonly OpeningsConfig _config;
    private readonly RevitActionRunner _runner;
    private readonly Discipline _discipline;
    private readonly RevitFileNameInfo _fileInfo;
    private readonly OpeningsServerClient _client;
    private View3D? _view;
    private HostReference? _highlightedHost;

    /// <summary>
    /// Состояние последнего <see cref="Reload"/> - нужно "Вставить"/"Вставить все согласованные",
    /// чтобы не ходить на сервер заново ради того, что уже загружено.
    /// </summary>
    private OpeningTaskViewDto[] _lastServerTasks = [];
    private IReadOnlyDictionary<string, ElementId> _lastPlacedElements = new Dictionary<string, ElementId>();
    private readonly Dictionary<string, ElementId> _realElements = [];

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private string _coordinateWarning = string.Empty;

    public bool HasCoordinateWarning => !string.IsNullOrWhiteSpace(CoordinateWarning);

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasError));

    partial void OnCoordinateWarningChanged(string value) => OnPropertyChanged(nameof(HasCoordinateWarning));

    public IncomingOpeningsNavigatorViewModel(
        OpeningsConfig config,
        RevitActionRunner runner,
        Discipline discipline,
        RevitFileNameInfo fileInfo,
        OpeningsServerClient client) {

        _config = config ?? throw new ArgumentNullException(nameof(config));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _discipline = discipline;
        _fileInfo = fileInfo ?? throw new ArgumentNullException(nameof(fileInfo));
        _client = client ?? throw new ArgumentNullException(nameof(client));

        Tasks = [];
        RefreshCommand = new ToolkitCommand(Refresh);
        SaveCommand = new ToolkitCommand(Save, () => Tasks.Any(task => task.IsDirty));
        InsertAllApprovedCommand = new ToolkitCommand(InsertAllApproved);
    }

    public ObservableCollection<IncomingOpeningTaskViewModel> Tasks { get; }

    public string ConfigName => _config.Name;

    /// <summary>
    /// Прорезать вырезающим семейством может и АР, и КР - обе дисциплины кроме согласования сами
    /// вставляют своё чистовое отверстие. (Ранее в этой же сессии ограничивалось только АР - решение
    /// оказалось ошибочным и отменено.)
    /// </summary>
    public bool CanCut => true;

    public IRelayCommand RefreshCommand { get; }

    public IRelayCommand SaveCommand { get; }

    /// <summary>
    /// Прорезает вырезающим семейством все загруженные задания, согласованные своей дисциплиной
    /// и ещё не прорезанные.
    /// </summary>
    public IRelayCommand InsertAllApprovedCommand { get; }

    /// <summary>
    /// Загружает задания с сервера и ставит их в модели. Вызывается только в контексте Revit API.
    /// </summary>
    public void Reload(UIApplication uiApplication) {
        if(uiApplication is null) {
            throw new ArgumentNullException(nameof(uiApplication));
        }

        ErrorText = string.Empty;
        var doc = uiApplication.ActiveUIDocument.Document;

        OpeningTaskViewDto[] serverTasks;
        try {
            serverTasks = Task.Run(() => _client.GetOpeningsAsync(_fileInfo.ProjectCode, _fileInfo.ObjectName))
                .GetAwaiter().GetResult();
        } catch(Exception exception) when(exception is InvalidOperationException or TimeoutException) {
            ErrorText = exception.Message;
            return;
        }

        // Сервер сравнивает fingerprint координат этой отправки с прошлым от той же дисциплины
        // и объекта - расхождение значит, что общие координаты в файле-отправителе могли
        // сместиться после прошлой отправки, и уже полученная геометрия могла уехать вместе с ними.
        var changedDisciplines = serverTasks
            .Where(task => task.SenderCoordinatesChanged)
            .Select(task => task.Discipline)
            .Distinct()
            .ToList();
        CoordinateWarning = changedDisciplines.Count == 0
            ? string.Empty
            : "Общие координаты изменились у отправителя после прошлой отправки: "
                + string.Join(", ", changedDisciplines)
                + ". Геометрия заданий этой дисциплины могла сместиться - сверьтесь с моделью.";

        var result = new IncomingOpeningPlacer(doc, _config).Place(serverTasks);

        // Только уборка: убирает чистовое отверстие, если решение сбросили или отклонили, и
        // сообщает, что уже прорезано и остаётся в силе. Само прорезание - по кнопке "Вставить"/
        // "Вставить все согласованные" (InsertAllApproved), не при каждом обновлении: это
        // необратимое в рамках навигатора изменение модели, пользователь должен решить сам.
        var revertResult = new RealOpeningReplacer(doc, _config).RevertRejected(serverTasks);

        // Отдельный вид с постоянной раскраской: красные задания, притушенные конструкции.
        // Реального инженерного элемента в этом файле нет - связь на него не заводится
        // (docs/OpeningsApi.md), поэтому зелёного слоя, как у исходящего навигатора, здесь нет.
        _view = NavigatorViewProvider.GetOrCreate(doc, "Входящие");
        _highlightedHost = null;
        NavigatorColorScheme.ApplyIncoming(doc, _view, [BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors]);

        _lastServerTasks = serverTasks;
        _lastPlacedElements = result.PlacedElements;
        _realElements.Clear();
        foreach(var pair in revertResult.RealElements) {
            _realElements[pair.Key] = pair.Value;
        }

        BuildRows(doc, result.MissingFamilies, revertResult.Reverted, placed: 0, missingRealFamilies: []);
    }

    /// <summary>
    /// Прорезает вырезающим семейством все загруженные задания, согласованные своей дисциплиной -
    /// по кнопке "Вставить все согласованные".
    /// </summary>
    private void InsertAllApproved() {
        if(!CanCut) {
            return;
        }

        _runner.Run(uiApplication => InsertApproved(uiApplication, _lastServerTasks));
    }

    /// <summary>
    /// Прорезает вырезающим семейством одно задание - по кнопке "Вставить" в строке.
    /// Вызывается из code-behind окна.
    /// </summary>
    public void Insert(IncomingOpeningTaskViewModel task) {
        if(task is null || !CanCut) {
            return;
        }

        _runner.Run(uiApplication => InsertApproved(uiApplication, [task.Task]));
    }

    private void InsertApproved(UIApplication uiApplication, IReadOnlyCollection<OpeningTaskViewDto> tasks) {
        var doc = uiApplication.ActiveUIDocument.Document;
        var placeResult = new RealOpeningReplacer(doc, _config)
            .PlaceApproved(tasks, _lastPlacedElements);

        foreach(var pair in placeResult.RealElements) {
            _realElements[pair.Key] = pair.Value;
        }

        BuildRows(doc, [], reverted: 0, placeResult.Placed, placeResult.MissingFamilies);

        // Прорезание молча ничего не делает, когда среди заданий нет согласованных обеими
        // дисциплинами: сводка в шапке об этом не говорит, а пользователь видит нажатую кнопку
        // и неизменившуюся модель. Поэтому причина пишется прямо в строку ошибки.
        if(placeResult.Placed > 0) {
            ErrorText = string.Empty;
        } else {
            ErrorText = "Не прорезано ни одного задания. " + GetSkipReason(placeResult, tasks);
        }
    }

    /// <summary>
    /// Возвращает причину, по которой прорезание ничего не сделало.
    /// </summary>
    /// <remarks>
    /// Причин несколько, и они требуют разных действий: одно дело - ждать решения второй дисциплины,
    /// другое - загрузить вырезающее семейство, третье - разобраться, почему под согласованным
    /// заданием нет конструкции. Общая фраза "прорезать нечего" отправляла бы искать не там.
    /// </remarks>
    private string GetSkipReason(RealOpeningResult result, IReadOnlyCollection<OpeningTaskViewDto> tasks) {
        if(result.MissingFamilies.Count > 0) {
            return "Не загружены вырезающие семейства: " + string.Join(", ", result.MissingFamilies) + ".";
        }

        if(result.SkippedNoHost > 0) {
            return $"У {result.SkippedNoHost} согласованных заданий под точкой вставки нет стены "
                + "или перекрытия: вырезающее семейство хостовое, вставлять его не во что. "
                + "Проверьте, совпадают ли общие координаты с файлом отправителя.";
        }

        if(result.SkippedNoTaskInstance > 0) {
            return $"У {result.SkippedNoTaskInstance} согласованных заданий нет экземпляра в модели - "
                + "их не удалось поставить при загрузке. Обновите окно и посмотрите, что в сводке.";
        }

        int cut = tasks.Count(task => _realElements.ContainsKey(task.TaskKey));
        if(cut == tasks.Count && tasks.Count > 0) {
            return "Всё уже прорезано.";
        }

        return $"Ждут решения второй дисциплины или отклонены: {result.SkippedNotApproved}. "
            + "Прорезают только задания, согласованные и ARC, и STR; общий статус считает сервер, "
            + "поэтому своё решение сначала отправьте кнопкой \"Сохранить\", а потом обновите окно.";
    }

    /// <summary>
    /// Перестраивает строки грида из уже загруженного состояния - без обращения к серверу.
    /// </summary>
    /// <remarks>
    /// "Вставить"/"Вставить все согласованные" вызывают это уже после того, как пользователь мог
    /// нажать "Согласовать"/"Отклонить" на других строках, но ещё не нажал "Сохранить" - решение
    /// живёт только в снесённом сейчас объекте строки. Без переноса несохранённое решение просто
    /// терялось бы молча при пересборке грида.
    /// </remarks>
    private void BuildRows(
        Document doc,
        IReadOnlyCollection<string> missingFamilies,
        int reverted,
        int placed,
        IReadOnlyCollection<string> missingRealFamilies) {

        var unsaved = Tasks
            .Where(task => task.IsDirty)
            .ToDictionary(task => task.TaskKey, task => (task.Status, task.Comment));

        Tasks.Clear();
        foreach(var task in _lastServerTasks.Where(task => !task.Deleted)) {
            // Прорезанное отверстие показываем и выделяем вместо задания - того к этому моменту
            // уже нет, PlaceApproved удалил его при прорезании.
            bool isCut = _realElements.TryGetValue(task.TaskKey, out var realId);
            ElementId elementId = isCut
                ? realId
                : _lastPlacedElements.TryGetValue(task.TaskKey, out var previewId) ? previewId : null;
            var instance = elementId is null ? null : doc.GetElement(elementId) as FamilyInstance;
            var host = GetHost(doc, instance, task.OpeningType);
            var technicalStatus = IncomingTaskTechnicalCheck.Evaluate(doc, instance);
            var row = new IncomingOpeningTaskViewModel(task, _discipline, elementId, host, technicalStatus, isCut);
            if(unsaved.TryGetValue(task.TaskKey, out var pending)) {
                row.Status = pending.Status;
                row.Comment = pending.Comment;
            }
            // Показать/Согласовать/Отклонить нарочно не команды: ICommand.CanExecute на кнопке внутри
            // ячейки грида (Command + CommandParameter="{Binding}") в этом проекте раз за разом
            // залипал на первом же (часто ещё до конца привязанном) значении и переставал реагировать
            // на дальнейшие изменения без ручного NotifyCanExecuteChanged, а сам он не давал
            // достаточно надёжной точки, откуда его звать. Кнопки в XAML вызывают обработчики Click
            // в code-behind напрямую, доступность решает обычная привязка IsEnabled к свойству строки -
            // никакой прослойки ICommand, которой есть что "залипать".
            row.PropertyChanged += (_, _) => SaveCommand.NotifyCanExecuteChanged();
            Tasks.Add(row);
        }

        Summary = BuildSummary(missingFamilies, reverted, placed, missingRealFamilies);
    }

    private string BuildSummary(
        IReadOnlyCollection<string> missingFamilies,
        int reverted,
        int placed,
        IReadOnlyCollection<string> missingRealFamilies) {

        var summary = new StringBuilder();
        summary.Append($"Заданий: {Tasks.Count}. ");
        summary.Append($"Ожидают моего решения: {Tasks.Count(task => OpeningDecisionStatus.IsSent(task.Status))}.");
        int approvedNotCut = Tasks.Count(task => task.CanInsert);
        if(approvedNotCut > 0) {
            summary.Append($" Согласовано, но не прорезано: {approvedNotCut} - кнопка \"Вставить\"/\"Вставить все согласованные\".");
        }
        if(placed > 0 || reverted > 0) {
            summary.Append($" Прорезано: {placed}, возвращено в задание: {reverted}.");
        }
        if(missingFamilies.Count > 0) {
            summary.Append($" Семейства не загружены: {string.Join(", ", missingFamilies)}.");
        }
        if(missingRealFamilies.Count > 0) {
            summary.Append($" Вырезающие семейства не загружены: {string.Join(", ", missingRealFamilies)}.");
        }

        return summary.ToString();
    }

    /// <summary>
    /// Выделяет задание в модели и показывает его на виде. Вызывается из code-behind окна
    /// по клику на "Показать".
    /// </summary>
    public void Show(IncomingOpeningTaskViewModel task) {
        Show(task?.ElementId, task?.HostElementId);
    }

    /// <summary>
    /// Выделяет и показывает хост задания - стену или перекрытие, которое оно пробивает.
    /// Вызывается из code-behind окна по клику на лупу.
    /// </summary>
    public void ShowHost(IncomingOpeningTaskViewModel task) {
        Show(task?.HostElementId, task?.HostElementId);
    }

    /// <summary>
    /// Выделяет элемент и подгоняет вид точно под его габарит, а не под всё выделение сразу
    /// (как простой <c>ShowElements</c>) - на плотной модели иначе не всегда понятно, куда
    /// пришла навигация. Хост при этом подсвечивается для контекста локально: связи на инженера
    /// здесь нет, элемент из связи выбирать не нужно.
    /// </summary>
    private void Show(ElementId? elementToSelect, ElementId? hostToHighlight) {
        if(elementToSelect is null) {
            return;
        }

        _runner.Run(uiApplication => {
            var uiDocument = uiApplication.ActiveUIDocument;
            var doc = uiDocument.Document;

            var view = _view ??= NavigatorViewProvider.GetOrCreate(doc, "Входящие");
            uiDocument.ActiveView = view;

            var next = hostToHighlight is null ? (HostReference?) null : new HostReference(hostToHighlight, null);
            HostHighlighter.Apply(doc, view, _highlightedHost, next);
            _highlightedHost = next;

            var element = doc.GetElement(elementToSelect);
            var box = element?.get_BoundingBox(null);
            if(box is not null) {
                double margin = UnitUtils.ConvertToInternalUnits(500, UnitTypeId.Millimeters);

                // SetSectionBox/IsSectionBoxActive меняют документ - без транзакции Revit падает
                // с "Attempt to modify the model outside of transaction".
                using var transaction = new Transaction(doc, "Зум навигатора заданий на отверстия");
                transaction.Start();
                view.SetSectionBox(new BoundingBoxXYZ {
                    Min = box.Min - new XYZ(margin, margin, margin),
                    Max = box.Max + new XYZ(margin, margin, margin)
                });
                view.IsSectionBoxActive = true;
                transaction.Commit();
            }

            var uiView = uiDocument.GetOpenUIViews().FirstOrDefault(openView => openView.ViewId == view.Id);
            uiView?.ZoomToFit();

            var ids = new List<ElementId> { elementToSelect };
            uiDocument.Selection.SetElementIds(ids);

            // Без этого рамка выделения иногда не перерисовывается сразу после программного
            // SetElementIds - видно только после того, как пользователь сам кликнет в вид.
            uiDocument.RefreshActiveView();
        });
    }

    private static Element? GetHost(Document doc, FamilyInstance? instance, OpeningType openingType) {
        if(instance is null) {
            return null;
        }

        // Вырезающее семейство хостовое - его собственный Host надёжнее геометрического поиска.
        // Семейство задания свободное (docs/OpeningTaskFamilies.md), у него Host всегда null,
        // и хост ищется по точке вставки, как и раньше.
        if(instance.Host is not null) {
            return instance.Host;
        }

        return instance.Location is LocationPoint locationPoint
            ? HostFinder.Find(doc, locationPoint.Point, openingType)
            : null;
    }

    /// <summary>
    /// Отмечает решение по заданию локально - на сервер ничего не уходит, пока не нажмут "Сохранить".
    /// Вызывается из code-behind окна по клику на "✓"/"✕".
    /// </summary>
    public void Approve(IncomingOpeningTaskViewModel task) {
        if(task?.CanApprove == true) {
            task.Status = "approved";
        }
    }

    public void Reject(IncomingOpeningTaskViewModel task) {
        if(task?.CanReject == true) {
            task.Status = "rejected";
        }
    }

    private void Refresh() {
        _runner.Run(Reload);
    }

    private void Save() {
        var dirty = Tasks.Where(task => task.IsDirty).ToList();
        if(dirty.Count == 0) {
            return;
        }

        var request = new OpeningStatusUpdateDto {
            Discipline = _discipline,
            ChangedBy = WindowsUser.Name,
            ChangedByDisplayName = WindowsUser.FullName,
            ChangedAt = DateTime.UtcNow,
            Items = dirty.Select(task => new OpeningStatusItemDto {
                TaskKey = task.TaskKey,
                Status = task.Status,
                Comment = task.Comment
            }).ToList()
        };

        OpeningStatusResultDto result;
        try {
            result = Task.Run(() => _client.UpdateOpeningStatusesAsync(request)).GetAwaiter().GetResult();
        } catch(Exception exception) when(exception is InvalidOperationException or TimeoutException) {
            ErrorText = exception.Message;
            return;
        }

        var failedKeys = new HashSet<string>(result.Failed.Select(failure => failure.TaskKey));
        foreach(var task in dirty.Where(task => !failedKeys.Contains(task.TaskKey))) {
            task.MarkSaved();
        }

        ErrorText = result.Failed.Count == 0
            ? string.Empty
            : "Не отправлено: " + string.Join("; ", result.Failed.Select(failure => $"{failure.TaskKey} - {failure.Message}"));

        SaveCommand.NotifyCanExecuteChanged();

        // Общий статус задания - дело сервера: он складывает решения АР и КР, и до перезагрузки
        // согласованное только что задание всё ещё выглядит как ожидающее решения, а "Вставить"
        // остаётся выключенной. Без этой перезагрузки после сохранения приходилось нажимать
        // "Обновить" руками, и выглядело это как неработающая кнопка.
        if(result.Failed.Count < dirty.Count) {
            _runner.Run(Reload);
        }
    }
}
