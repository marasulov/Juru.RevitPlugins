namespace Juru.RevitPlugins.Core.FileNaming;

/// <summary>
/// Расширения для <see cref="Discipline"/>.
/// </summary>
public static class DisciplineExtensions {
    /// <summary>
    /// Возвращает роль файла указанной дисциплины в обмене заданиями на отверстия.
    /// </summary>
    /// <remarks>
    /// Фасады (<see cref="Discipline.FAD"/>) в обмене не участвуют: отверстия в них не размещаются.
    /// </remarks>
    public static DocRole GetDocRole(this Discipline discipline) {
        switch(discipline) {
            case Discipline.WSS:
            case Discipline.ELE:
            case Discipline.HVC:
                return DocRole.Mep;
            case Discipline.ARC:
                return DocRole.Arc;
            case Discipline.STR:
                return DocRole.Str;
            default:
                return DocRole.Unknown;
        }
    }

    /// <summary>
    /// Проверяет, выдаёт ли дисциплина задания на отверстия.
    /// </summary>
    public static bool IssuesOpeningTasks(this Discipline discipline) {
        return discipline.GetDocRole() == DocRole.Mep;
    }

    /// <summary>
    /// Проверяет, принимает ли дисциплина задания на отверстия и размещает чистовые отверстия.
    /// </summary>
    public static bool ReceivesOpeningTasks(this Discipline discipline) {
        var role = discipline.GetDocRole();
        return role == DocRole.Arc || role == DocRole.Str;
    }
}
