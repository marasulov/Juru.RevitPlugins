using System;
using System.Collections.Generic;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Core.Localization;
using Juru.RevitPlugins.Openings.Models.Navigator;
using Juru.RevitPlugins.Openings.Models.Server.Dto;

namespace Juru.RevitPlugins.Openings.ViewModels.Navigator;

/// <summary>
/// Строка навигатора: одно задание на отверстие.
/// </summary>
/// <remarks>
/// Значения читаются из модели один раз при загрузке: список открывается поверх Revit, и обращаться
/// к модели при каждой перерисовке строки нельзя - окно немодальное и работает вне контекста API.
/// </remarks>
public class OpeningTaskViewModel {
    /// <param name="task">Задание из модели с уже посчитанным техническим состоянием.</param>
    /// <param name="decision">
    /// Запись этого задания на сервере, найденная по ключу, либо <c>null</c>, если её там нет.
    /// </param>
    /// <param name="serverChecked">
    /// Сервер был опрошен. Без этого пустое решение означает не "задание не отправляли", а
    /// "спросить было не у кого" - разница существенная, и в колонках она видна.
    /// </param>
    public OpeningTaskViewModel(
        OpeningTask task,
        PluginLocalization localization,
        OpeningTaskViewDto? decision = null,
        bool serverChecked = false) {

        if(task is null) { throw new ArgumentNullException(nameof(task)); }

        Task = task;
        Decision = decision;
        ArcDecisionText = GetDecisionText(decision?.ArcDecision, decision, serverChecked, localization);
        StrDecisionText = GetDecisionText(decision?.StrDecision, decision, serverChecked, localization);
        DecisionComment = GetDecisionComment(decision);
        IsArcRejected = decision?.ArcDecision?.Status == "rejected";
        IsStrRejected = decision?.StrDecision?.Status == "rejected";
        IsRejected = IsArcRejected || IsStrRejected;
        IsApproved = decision?.Status == "approved";
        Status = task.Status.GetDescription(localization);
        NeedsAttention = task.Status.NeedsAttention();
        SortOrder = NeedsAttention ? 0 : 1;
        Id = task.Id.ToString();
        Date = task.Date;
        MepSystem = task.MepSystem;
        Description = task.Description;
        DateGroupText = localization.GetString("Navigator.DateGroupFormat", Date);
        Author = task.Author;
        Size = GetSizeText(task);
        Elevation = ToMillimeters(task.GetElevation());
        CenterElevation = ToMillimeters(task.GetCenterElevation());
        Host = GetHostText(task.Host);
        HostId = task.Host?.Id.ToString() ?? string.Empty;
        SearchIndex = string.Join(" ",
            Id, MepSystem, Size, Status, Host, HostId, Description, Author,
            ArcDecisionText, StrDecisionText, DecisionComment);
    }

    /// <summary>
    /// Задание, которое показывает строка.
    /// </summary>
    public OpeningTask Task { get; }

    /// <summary>
    /// Состояние задания, как оно называется для пользователя. По нему список группируется.
    /// </summary>
    public string Status { get; }

    /// <summary>
    /// Задание требует внимания инженера.
    /// </summary>
    public bool NeedsAttention { get; }

    /// <summary>
    /// Порядок группы в списке: проблемные задания идут первыми.
    /// </summary>
    public int SortOrder { get; }

    public string Id { get; }

    public string Date { get; }

    /// <summary>
    /// Дата с зашитым префиксом - вторая, вложенная группировка в навигаторе, как в Bim4Everyone:
    /// внутри каждого статуса задания разбиты по дате выдачи.
    /// </summary>
    public string DateGroupText { get; }

    /// <summary>
    /// Имя инженерной системы, для которой выдано задание.
    /// </summary>
    public string MepSystem { get; }

    /// <summary>
    /// Описание: элементы, из пересечения которых возникло задание.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Габариты задания: диаметр либо ширина и высота, в мм.
    /// </summary>
    public string Size { get; }

    /// <summary>
    /// Отметка задания в мм, как она записана в семейство.
    /// </summary>
    public string Elevation { get; }

    /// <summary>
    /// Отметка оси задания в мм.
    /// </summary>
    public string CenterElevation { get; }

    /// <summary>
    /// Конструкция из связи, которую пробивает задание.
    /// </summary>
    public string Host { get; }

    /// <summary>
    /// Id хоста в его собственном документе - отдельной колонкой от имени, как у Bim4Everyone.
    /// </summary>
    public string HostId { get; }

    /// <summary>
    /// Хост найден - кнопка "Хост" доступна. Обычная привязка <c>IsEnabled</c>, не <c>CanExecute</c>:
    /// см. замечание в code-behind окна про ловушку с командой внутри шаблонной колонки.
    /// </summary>
    public bool CanShowHost => Task.Host is not null;

    public string Author { get; }

    /// <summary>
    /// Запись задания на сервере: обе оценки и комментарии. <c>null</c>, если сервер не отвечал
    /// либо задание туда ещё не отправляли.
    /// </summary>
    public OpeningTaskViewDto? Decision { get; }

    /// <summary>
    /// Решение архитектуры по этому заданию.
    /// </summary>
    /// <remarks>
    /// Решения показываются двумя колонками, а не одним общим статусом: инженеру нужно видеть,
    /// кто именно ещё не ответил, - "согласовано частично" не говорит, кого торопить.
    /// </remarks>
    public string ArcDecisionText { get; }

    /// <summary>
    /// Решение конструкций по этому заданию.
    /// </summary>
    public string StrDecisionText { get; }

    /// <summary>
    /// Причины отклонения обеих дисциплин - без них колонка "отклонено" не говорит, что чинить.
    /// </summary>
    public string DecisionComment { get; }

    /// <summary>
    /// Задание отклонила архитектура - подсвечивает колонку <see cref="ArcDecisionText" />.
    /// </summary>
    public bool IsArcRejected { get; }

    /// <summary>
    /// Задание отклонили конструкции - подсвечивает колонку <see cref="StrDecisionText" />.
    /// </summary>
    public bool IsStrRejected { get; }

    /// <summary>
    /// Задание отклонила хотя бы одна дисциплина.
    /// </summary>
    public bool IsRejected { get; }

    /// <summary>
    /// Задание согласовали обе дисциплины.
    /// </summary>
    public bool IsApproved { get; }

    /// <summary>
    /// Строка для поиска по гриду - объединяет колонки, по которым имеет смысл искать.
    /// </summary>
    public string SearchIndex { get; }

    /// <summary>
    /// Возвращает решение одной дисциплины по-русски.
    /// </summary>
    /// <remarks>
    /// Три пустых случая различаются намеренно. Сервер не спрашивали - решения неизвестны, и писать
    /// "ожидает" было бы враньём. Спросили, но записи нет - задание есть в модели, а на сервер его
    /// не отправляли. Запись есть и решение сброшено сервером - это не то же самое, что никем ещё
    /// не рассмотренное задание: его уже согласовали, а потом геометрия изменилась.
    /// </remarks>
    private static string GetDecisionText(
        OpeningDecisionDto? decision,
        OpeningTaskViewDto? task,
        bool serverChecked,
        PluginLocalization localization) {

        if(!serverChecked) {
            return "—";
        }

        if(task is null) {
            return localization.GetString("Navigator.Decision.NotSent");
        }

        return decision?.Status switch {
            "approved" => localization.GetString("Navigator.Decision.Approved"),
            "rejected" => localization.GetString("Navigator.Decision.Rejected"),
            "sent" when decision.ChangedAfterApproval => localization.GetString("Navigator.Decision.ResetChanged"),
            "sent" => localization.GetString("Navigator.Decision.Pending"),
            _ => localization.GetString("Navigator.Decision.Pending")
        };
    }

    private static string GetDecisionComment(OpeningTaskViewDto? task) {
        if(task is null) {
            return string.Empty;
        }

        var comments = new List<string>();
        Add("ARC", task.ArcDecision);
        Add("STR", task.StrDecision);
        return string.Join("; ", comments);

        void Add(string discipline, OpeningDecisionDto? decision) {
            if(decision?.Status == "rejected" && !string.IsNullOrWhiteSpace(decision.Comment)) {
                comments.Add($"{discipline}: {decision.Comment}");
            }
        }
    }

    private static string GetSizeText(OpeningTask task) {
        var (width, height, diameter, _) = task.GetSize();
        return task.IsRound
            ? $"⌀{ToMillimeters(diameter)}"
            : $"{ToMillimeters(width)}×{ToMillimeters(height)}";
    }

    /// <summary>
    /// Возвращает описание конструкции-основы вместе с файлом, из которого она пришла:
    /// одно и то же имя стены встречается в архитектуре и в конструкциях.
    /// </summary>
    private static string GetHostText(Element host) {
        return host is null ? string.Empty : $"{host.Name} ({host.Document.Title})";
    }

    private static string ToMillimeters(double feet) {
        return UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters).ToString("0");
    }
}
