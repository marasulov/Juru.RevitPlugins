using System;

using Autodesk.Revit.DB;

using CommunityToolkit.Mvvm.ComponentModel;

using Juru.RevitPlugins.Core.FileNaming;
using Juru.RevitPlugins.Openings.Models.Navigator;
using Juru.RevitPlugins.Openings.Models.Server.Dto;

namespace Juru.RevitPlugins.Openings.ViewModels.Navigator;

/// <summary>
/// Строка навигатора согласования: одно задание, полученное с сервера, и решение своей дисциплины
/// по нему - ещё не отправленное, пока пользователь не нажмёт "Сохранить".
/// </summary>
public partial class IncomingOpeningTaskViewModel : ObservableObject {
    private string _originalStatus;
    private string _originalComment;

    [ObservableProperty]
    private string _status;

    [ObservableProperty]
    private string _comment;

    public IncomingOpeningTaskViewModel(
        OpeningTaskViewDto task,
        Discipline ownDiscipline,
        ElementId elementId,
        Element? host,
        OpeningTaskStatus? technicalStatus,
        bool isCut) {

        Task = task ?? throw new ArgumentNullException(nameof(task));
        ElementId = elementId;
        HostElementId = host?.Id;
        HostName = host?.Name ?? string.Empty;
        IsCut = isCut;
        CanCut = true;

        var ownDecision = ownDiscipline == Discipline.ARC ? task.ArcDecision : task.StrDecision;
        _originalStatus = ownDecision.Status;
        _originalComment = ownDecision.Comment ?? string.Empty;
        _status = _originalStatus;
        _comment = _originalComment;

        SizeText = GetSizeText(task);
        OverallStatusText = GetOverallStatusText(task.Status);
        OtherDisciplineText = GetOtherDisciplineText(task, ownDiscipline);
        JustReset = ownDecision.ChangedAfterApproval;
        TechnicalStatusText = GetTechnicalStatusText(technicalStatus, task.IsUnited);
        TechnicalStatusNeedsAttention = technicalStatus is
            OpeningTaskStatus.UnacceptableConstructions or OpeningTaskStatus.DifferentConstructions;

        SearchIndex = string.Join(" ",
            ElementIdText, HostElementIdText, HostName, OpeningTypeText, SizeText, LevelName, MepSystem,
            SenderDiscipline, SentByDisplayName, OverallStatusText, TechnicalStatusText, Description);
    }

    public OpeningTaskViewDto Task { get; }

    /// <summary>
    /// Экземпляр в модели, соответствующий заданию - <c>null</c>, если поставить не удалось
    /// (семейство не загружено).
    /// </summary>
    public ElementId ElementId { get; }

    public bool CanShow => ElementId is not null;

    /// <summary>
    /// Стена или перекрытие, которое пробивает задание - найдено геометрией по точке вставки
    /// (<see cref="Server.HostFinder"/>), а не хостом экземпляра: семейство задания свободное.
    /// </summary>
    public ElementId? HostElementId { get; }

    public bool CanShowHost => HostElementId is not null;

    /// <summary>
    /// Задание уже прорезано вырезающим семейством - взято на момент загрузки, до правок в этом
    /// окне (согласовали/отклонили только что - ещё не прорезано, пока не нажали "Вставить").
    /// </summary>
    public bool IsCut { get; }

    /// <summary>
    /// "Вставить" доступна: согласовали обе дисциплины (общий <c>Task.Status</c>, как пришло
    /// с сервера при загрузке - отклонение любой стороны побеждает по контракту, поэтому
    /// решения только своей дисциплины недостаточно) и ещё не прорезано.
    /// </summary>
    public bool CanInsert => CanCut && !IsCut && Task.Status == "approved";

    /// <summary>
    /// Прорезать может и АР, и КР (см. <see cref="IncomingOpeningsNavigatorViewModel.CanCut"/>).
    /// </summary>
    public bool CanCut { get; }

    /// <summary>
    /// Строка для поиска по гриду - объединяет колонки, по которым имеет смысл искать.
    /// </summary>
    public string SearchIndex { get; }

    /// <summary>
    /// Имя хоста - как в навигаторе Bim4Everyone, отдельной колонкой от его Id.
    /// </summary>
    public string HostName { get; }

    public string HostElementIdText => HostElementId?.ToString() ?? string.Empty;

    /// <summary>
    /// Id самого экземпляра задания в этом файле - для сверки с моделью, как у Bim4Everyone.
    /// </summary>
    public string ElementIdText => ElementId?.ToString() ?? string.Empty;

    /// <summary>
    /// Технический статус, пересчитанный локально по конструкциям файла АР/КР
    /// (<see cref="Server.IncomingTaskTechnicalCheck"/>) плюс "объединённое", переданное флагом
    /// с ВИС-стороны - в отличие от одобрения, это не решение человека, а факт про геометрию.
    /// </summary>
    public string TechnicalStatusText { get; }

    /// <summary>
    /// Технический статус требует внимания - недопустимая или неоднозначная конструкция.
    /// "Объединённое" внимания не требует, это не проблема, только факт.
    /// </summary>
    public bool TechnicalStatusNeedsAttention { get; }

    /// <summary>
    /// Группа по техническому статусу - самый верхний уровень группировки: проблемные конструкции
    /// должны быть видны сразу, как у исходящего навигатора и у Bim4Everyone, а не потеряны
    /// среди десятков обычных заданий, сгруппированных только по тому, кто их выдал.
    /// </summary>
    public string TechnicalGroupText => $"Конструкция: {TechnicalStatusText}";

    /// <summary>
    /// Порядок группы по техническому статусу: проблемные - первыми.
    /// </summary>
    public int TechnicalSortOrder => TechnicalStatusNeedsAttention ? 0 : 1;

    public string TaskKey => Task.TaskKey;

    public string OpeningTypeText => Task.OpeningType.ToString();

    public string SizeText { get; }

    public string LevelName => Task.LevelName;

    public string MepSystem => Task.MepSystem;

    public string SenderDiscipline => Task.Discipline.ToString();

    public string SentByDisplayName => Task.SentByDisplayName;

    public string SentAtText => Task.SentAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm");

    public string Description => Task.Description;

    /// <summary>
    /// Общий статус (сервер считает из обоих решений), как он назывался в обсуждении с пользователем.
    /// </summary>
    public string OverallStatusText { get; }

    /// <summary>
    /// Группировка навигатора по трём уровням - откуда пришло задание, его статус, дата выдачи -
    /// как в навигаторе Bim4Everyone (группы с раскрытием и счётчиком в каждой). Префикс зашит в
    /// текст группы, а не в отдельный шаблон уровня: <see cref="Views.Navigator.IncomingOpeningsNavigatorWindow"/>
    /// использует один и тот же стиль заголовка для всех трёх уровней.
    /// </summary>
    public string SenderGroupText => $"Откуда: {SenderDiscipline}";

    public string StatusGroupText => $"Статус: {OverallStatusText}";

    public string DateGroupText => $"Дата: {Task.SentAt.ToLocalTime():dd.MM.yyyy}";

    /// <summary>
    /// Решение другой дисциплины - для контекста: видно, что уже сделал АР, пока смотришь из КР, и наоборот.
    /// </summary>
    public string OtherDisciplineText { get; }

    /// <summary>
    /// Своё решение только что сбросили в "ожидает" - геометрия заметно изменилась после согласования.
    /// Показывается отдельно от простого "ещё не рассмотрено": это другая история.
    /// </summary>
    public bool JustReset { get; }

    /// <summary>
    /// Решение изменено в окне, но ещё не отправлено на сервер.
    /// </summary>
    public bool IsDirty => Status != _originalStatus || (Status == "rejected" && Comment != _originalComment);

    /// <summary>
    /// Отклонить нельзя без комментария - сервер такую пачку всё равно отклонит.
    /// </summary>
    public bool CanReject => !string.IsNullOrWhiteSpace(Comment);

    /// <summary>
    /// Пока в комментарии есть текст - это заготовка отклонения, и согласовать нельзя, не стерев её:
    /// иначе кнопки одновременно показывали бы оба решения как доступные "прямо сейчас".
    /// </summary>
    public bool CanApprove => string.IsNullOrWhiteSpace(Comment);

    /// <summary>
    /// Решение действительно принято - кнопки подсвечиваются по этому признаку, а не по тому,
    /// можно ли сейчас нажать: набранный, но не отправленный кнопкой комментарий ничего не решает.
    /// </summary>
    public bool IsApproved => Status == "approved";

    public bool IsRejected => Status == "rejected";

    /// <summary>
    /// Своё решение по-человечески - подпись рядом с кнопками ✓/✕.
    /// </summary>
    public string OwnStatusText => GetOverallStatusText(Status);

    /// <summary>
    /// Отмечает решение отправленным успешно - вызывается после сохранения пачки. Status/Comment
    /// не трогает - в них уже лежит то, что отправили; "грязным" поле станет заново, только если
    /// решение поменяют ещё раз.
    /// </summary>
    public void MarkSaved() {
        _originalStatus = Status;
        _originalComment = Comment;
        OnPropertyChanged(nameof(IsDirty));
    }

    partial void OnStatusChanged(string value) {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(IsApproved));
        OnPropertyChanged(nameof(IsRejected));
        OnPropertyChanged(nameof(OwnStatusText));
    }

    partial void OnCommentChanged(string value) {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CanReject));
        OnPropertyChanged(nameof(CanApprove));
    }

    private static string GetSizeText(OpeningTaskViewDto task) {
        bool isRound = task.DiameterMm is not null;
        return isRound
            ? $"⌀{task.DiameterMm:0}"
            : $"{task.WidthMm:0}×{task.HeightMm:0}";
    }

    private static string GetOverallStatusText(string status) {
        return status switch {
            "sent" => "Ожидает согласования",
            "partiallyApproved" => "Согласовано частично",
            "approved" => "Согласовано",
            "rejected" => "Отклонено",
            _ => status
        };
    }

    private static string GetTechnicalStatusText(OpeningTaskStatus? technicalStatus, bool isUnited) {
        // Приоритет как на стороне ВИС: недопустимая конструкция важнее неоднозначной, и обе -
        // важнее того, что задание просто объединённое (это не проблема, факт для контекста).
        if(technicalStatus is OpeningTaskStatus.UnacceptableConstructions or OpeningTaskStatus.DifferentConstructions) {
            return technicalStatus.Value.GetDescription();
        }
        if(isUnited) {
            return OpeningTaskStatus.United.GetDescription();
        }
        return technicalStatus?.GetDescription() ?? "Нет данных";
    }

    private static string GetOtherDisciplineText(OpeningTaskViewDto task, Discipline ownDiscipline) {
        var (label, decision) = ownDiscipline == Discipline.ARC
            ? ("КР", task.StrDecision)
            : ("АР", task.ArcDecision);
        return $"{label}: {GetOverallStatusText(decision.Status)}";
    }
}
