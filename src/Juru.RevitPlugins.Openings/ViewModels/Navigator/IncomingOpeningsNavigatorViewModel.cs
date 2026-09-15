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

    /// <summary>
    /// Что сталось с файлом при последнем прорезании - приписка к сводке.
    /// </summary>
    private string _saveNote = string.Empty;
    private IReadOnlyDictionary<string, ElementId> _lastPlacedElements = new Dictionary<string, ElementId>();
    private readonly Dictionary<string, ElementId> _realElements = [];

    /// <summary>
    /// Отверстия, прорезанные по заданиям, которые инженер потом удалил, по ключу задания.
    /// </summary>
    /// <remarks>
    /// Отдельно от <see cref="_realElements"/>: те действуют, эти - остатки от заданий, которых
    /// больше нет. Обновление их не трогает, убрать отверстие может только согласующий, увидев
    /// в списке, какое именно (docs/OpeningsApi.md, "Удалённые задания").
    /// </remarks>
    private readonly Dictionary<string, ElementId> _deletedRealElements = [];

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

        // Строки пересобираются при каждой загрузке и прорезании, а с ними меняется и ответ на
        // вопрос "есть ли что отправлять". Без этого "Сохранить" оставалась в том состоянии,
        // в котором её застала прошлая пересборка.
        Tasks.CollectionChanged += (_, _) => SaveCommand.NotifyCanExecuteChanged();
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
        // Приписка о сохранении относилась к прошлому прорезанию: после перезагрузки она сказала бы
        // про этот заход то, чего в нём не было.
        _saveNote = string.Empty;
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
        try {
            NavigatorColorScheme.ApplyIncoming(doc, _view, [BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors]);
        } catch(Autodesk.Revit.Exceptions.ApplicationException) {
            // Раскраска - удобство, а не работа навигатора: из-за неё нельзя терять весь список
            // заданий, см. исходящий навигатор.
        }

        _lastServerTasks = serverTasks;
        _lastPlacedElements = result.PlacedElements;
        _realElements.Clear();
        foreach(var pair in revertResult.RealElements) {
            _realElements[pair.Key] = pair.Value;
        }

        _deletedRealElements.Clear();
        foreach(var pair in revertResult.DeletedRealElements) {
            _deletedRealElements[pair.Key] = pair.Value;
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

        _runner.Run(uiApplication => InsertApproved(uiApplication, taskKeys: null));
    }

    /// <summary>
    /// Прорезает вырезающим семейством одно задание - по кнопке "Вставить" в строке.
    /// Вызывается из code-behind окна.
    /// </summary>
    public void Insert(IncomingOpeningTaskViewModel task) {
        if(task is null || !CanCut) {
            return;
        }

        string taskKey = task.TaskKey;
        _runner.Run(uiApplication => InsertApproved(uiApplication, [taskKey]));
    }

    /// <summary>
    /// Прорезает согласованные задания: сначала отправляет решения на сервер и перечитывает
    /// статусы, потом заменяет семейства.
    /// </summary>
    /// <param name="taskKeys">
    /// Ключи заданий, которые прорезаем, либо <c>null</c> - все загруженные.
    /// </param>
    /// <remarks>
    /// Порядок именно такой, и он важен. Общий статус задания считает сервер из решений АР и КР,
    /// и своё "согласовано", пока оно не отправлено и не прочитано обратно, для прорезания не
    /// считается: кнопка нажималась, а модель не менялась. Поэтому отправка решений - то же самое,
    /// что кнопка "Сохранить", - и перезагрузка идут здесь сами, до замены семейств. Работает это
    /// и по ключам, а не по объектам заданий: после перезагрузки объекты уже другие.
    /// </remarks>
    private void InsertApproved(UIApplication uiApplication, IReadOnlyCollection<string> taskKeys) {
        var (_, sendError) = SendDecisions();
        if(sendError is not null) {
            // Прорезать по статусам, которых сервер не знает, нельзя: это и есть та самая
            // рассинхронизация, ради которой решения отправляются первыми.
            ErrorText = sendError;
            return;
        }

        // Свежие статусы - и заодно заново поставленные задания и убранные отклонённые отверстия.
        Reload(uiApplication);
        if(HasError) {
            return;
        }

        var tasks = _lastServerTasks
            .Where(task => taskKeys is null || taskKeys.Contains(task.TaskKey))
            .ToList();

        var doc = uiApplication.ActiveUIDocument.Document;
        var placeResult = new RealOpeningReplacer(doc, _config)
            .PlaceApproved(tasks, _lastPlacedElements);

        foreach(var pair in placeResult.RealElements) {
            _realElements[pair.Key] = pair.Value;
        }

        // Файл сохраняется сразу, а не оставляется на усмотрение пользователя: решение уже ушло
        // на сервер, и там задание согласовано. Выйди согласующий без сохранения - вырезающего
        // семейства в файле нет, а на сервере всё в порядке, и заметить это некому: своё
        // согласованное задание он больше не разбирает.
        string saveError = placeResult.Placed > 0 ? SaveModel(doc) : null;
        _saveNote = placeResult.Placed > 0 && saveError is null
            ? doc.IsWorkshared ? " Изменения синхронизированы." : " Файл сохранён."
            : string.Empty;

        BuildRows(doc, [], reverted: 0, placeResult.Placed, placeResult.MissingFamilies);

        // Прорезание молча ничего не делает, когда среди заданий нет согласованных обеими
        // дисциплинами: сводка в шапке об этом не говорит, а пользователь видит нажатую кнопку
        // и неизменившуюся модель. Поэтому причина пишется прямо в строку ошибки.
        if(placeResult.Placed == 0) {
            ErrorText = "Не прорезано ни одного задания. " + GetSkipReason(placeResult, tasks);
        } else if(saveError is not null) {
            ErrorText = $"Прорезано заданий: {placeResult.Placed}, но файл не сохранён: {saveError} "
                + "Сохраните файл сами - иначе на сервере задание останется согласованным, "
                + "а вырезающего семейства в файле не будет.";
        } else {
            ErrorText = string.Empty;
        }
    }

    /// <summary>
    /// Сохраняет файл после прорезания. Возвращает <c>null</c>, если сохранён, иначе причину:
    /// центральная модель бывает занята, а файл - открыт только для чтения, и в этом случае
    /// согласующий должен узнать, что сохранять придётся самому.
    /// </summary>
    private static string SaveModel(Document doc) {
        try {
            DocumentSaver.SaveOrSynchronize(doc);
            return null;
        } catch(Autodesk.Revit.Exceptions.ApplicationException exception) {
            return exception.Message;
        } catch(InvalidOperationException exception) {
            return exception.Message;
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
            + "Прорезают только задания, согласованные и ARC, и STR; общий статус считает сервер. "
            + "Свои решения уже отправлены и статусы перечитаны - значит, дело за второй дисциплиной.";
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
        // Удалённое задание попадает в список, только пока по нему стоит прорезанное отверстие:
        // у остальных убирать нечего, их экземпляр снимает загрузка.
        foreach(var task in _lastServerTasks.Where(task => !task.Deleted || _deletedRealElements.ContainsKey(task.TaskKey))) {
            // Прорезанное отверстие показываем и выделяем вместо задания - того к этому моменту
            // уже нет, PlaceApproved удалил его при прорезании.
            var cutElements = task.Deleted ? _deletedRealElements : _realElements;
            bool isCut = cutElements.TryGetValue(task.TaskKey, out var realId);
            ElementId elementId = isCut
                ? realId
                : _lastPlacedElements.TryGetValue(task.TaskKey, out var previewId) ? previewId : null;
            var instance = elementId is null ? null : doc.GetElement(elementId) as FamilyInstance;
            var host = GetHost(doc, instance, task.OpeningType);
            var technicalStatus = IncomingTaskTechnicalCheck.Evaluate(doc, instance);
            var row = new IncomingOpeningTaskViewModel(
                task, _discipline, elementId, host, technicalStatus, isCut, task.Deleted);
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
            summary.Append(_saveNote);
        }
        int deleted = Tasks.Count(task => task.CanRemove);
        if(deleted > 0) {
            summary.Append($" Инженер удалил заданий, по которым отверстия уже прорезаны: {deleted}"
                + " - проверьте их и уберите кнопкой \"Убрать\" в строке.");
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
    /// Убирает из модели чистовое отверстие задания, которое инженер у себя удалил. Вызывается
    /// из code-behind окна по клику на "Убрать".
    /// </summary>
    /// <remarks>
    /// Сервер об этом не спрашивают и ему ничего не отправляют: задание уже помечено удалённым
    /// на его стороне, а прорезано отверстие или нет - факт про эту модель. Решения по заданию
    /// в записи остаются, они и есть история того, что отверстие когда-то согласовали.
    /// </remarks>
    public void Remove(IncomingOpeningTaskViewModel task) {
        if(task is null || !task.CanRemove) {
            return;
        }

        string taskKey = task.TaskKey;
        _runner.Run(uiApplication => {
            var doc = uiApplication.ActiveUIDocument.Document;
            if(!_deletedRealElements.TryGetValue(taskKey, out var realId)) {
                return;
            }

            if(!ConfirmRemove(task)) {
                return;
            }

            if(doc.GetElement(realId) is not null) {
                using var transaction = new Transaction(doc, "Удаление отверстия по удалённому заданию");
                transaction.Start();
                doc.Delete(realId);
                transaction.Commit();
            }

            _deletedRealElements.Remove(taskKey);
            BuildRows(doc, missingFamilies: [], reverted: 0, placed: 0, missingRealFamilies: []);
        });
    }

    private static bool ConfirmRemove(IncomingOpeningTaskViewModel task) {
        var confirm = TaskDialog.Show("Удалённое задание",
            $"Убрать из модели отверстие {task.SizeText} мм ({task.OpeningTypeText}) на уровне "
            + $"{task.LevelName}?\n\n"
            + $"Инженер ({task.SenderDiscipline}, {task.SentByDisplayName}) удалил задание, "
            + "по которому оно прорезано. Конструкция закроется обратно.",
            TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No, TaskDialogResult.No);
        return confirm == TaskDialogResult.Yes;
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
        var (sent, error) = SendDecisions();
        ErrorText = error ?? string.Empty;

        // Общий статус задания - дело сервера: он складывает решения АР и КР, и до перезагрузки
        // согласованное только что задание всё ещё выглядит как ожидающее решения, а "Вставить"
        // остаётся выключенной. Без этой перезагрузки после сохранения приходилось нажимать
        // "Обновить" руками, и выглядело это как неработающая кнопка.
        if(sent > 0) {
            _runner.Run(Reload);
        }
    }

    /// <summary>
    /// Отправляет отмеченные в окне решения на сервер одной пачкой.
    /// </summary>
    /// <returns>
    /// Сколько решений ушло и что не удалось; причина <c>null</c> значит, что ушло всё либо
    /// отправлять было нечего.
    /// </returns>
    private (int Sent, string Error) SendDecisions() {
        var dirty = Tasks.Where(task => task.IsDirty).ToList();
        if(dirty.Count == 0) {
            return (0, null);
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
            return (0, exception.Message);
        }

        var failedKeys = new HashSet<string>(result.Failed.Select(failure => failure.TaskKey));
        foreach(var task in dirty.Where(task => !failedKeys.Contains(task.TaskKey))) {
            task.MarkSaved();
        }

        SaveCommand.NotifyCanExecuteChanged();

        string error = result.Failed.Count == 0
            ? null
            : "Не отправлено: " + string.Join("; ", result.Failed.Select(failure => $"{failure.TaskKey} - {failure.Message}"));

        return (dirty.Count - result.Failed.Count, error);
    }
}
