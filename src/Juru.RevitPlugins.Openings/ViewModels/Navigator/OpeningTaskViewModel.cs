using System;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Openings.Models.Navigator;

namespace Juru.RevitPlugins.Openings.ViewModels.Navigator;

/// <summary>
/// Строка навигатора: одно задание на отверстие.
/// </summary>
/// <remarks>
/// Значения читаются из модели один раз при загрузке: список открывается поверх Revit, и обращаться
/// к модели при каждой перерисовке строки нельзя - окно немодальное и работает вне контекста API.
/// </remarks>
public class OpeningTaskViewModel {
    public OpeningTaskViewModel(OpeningTask task) {
        if(task is null) { throw new ArgumentNullException(nameof(task)); }

        Task = task;
        Status = task.Status.GetDescription();
        NeedsAttention = task.Status.NeedsAttention();
        SortOrder = NeedsAttention ? 0 : 1;
        Id = task.Id.ToString();
        Date = task.Date;
        MepSystem = task.MepSystem;
        Description = task.Description;
        Author = task.Author;
        Size = GetSizeText(task);
        Elevation = ToMillimeters(task.GetElevation());
        CenterElevation = ToMillimeters(task.GetCenterElevation());
        Host = GetHostText(task.Host);
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

    public string Author { get; }

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
