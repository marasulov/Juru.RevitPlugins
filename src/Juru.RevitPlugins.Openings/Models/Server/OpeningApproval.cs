using System.Linq;

using Juru.RevitPlugins.Core.FileNaming;
using Juru.RevitPlugins.Openings.Models.Server.Dto;

namespace Juru.RevitPlugins.Openings.Models.Server;

/// <summary>
/// Отвечает, согласовано ли задание всеми, чьё решение по нему требуется.
/// </summary>
/// <remarks>
/// Ответ даёт сервер - общий статус задания, - и он остаётся главным источником: считать статус
/// заново значит держать две реализации одного правила (см. "Два независимых статуса"
/// в <c>docs/OpeningsApi.md</c>). Но общий статус учитывает состав согласующих
/// (<see cref="OpeningTaskViewDto.Approvers"/>), а он появился в контракте позже самих решений:
/// пока сервер считает статус по обеим дисциплинам, задание объекта, где согласует только АР,
/// навсегда остаётся <c>partiallyApproved</c> и не прорезается - ждёт решения, которое некому
/// принять. Поэтому состав согласующих, если сервер его вернул, проверяется и здесь: он не
/// расширяет права (отклонение по-прежнему перевешивает), а только не даёт ждать лишнего.
/// </remarks>
public static class OpeningApproval {
    /// <summary>
    /// Задание согласовано: ни одна из требуемых дисциплин его не отклонила и все они ответили
    /// "согласовано".
    /// </summary>
    public static bool IsApproved(OpeningTaskViewDto task) {
        if(task is null) {
            return false;
        }

        if(OpeningDecisionStatus.IsRejected(task.ArcDecision?.Status)
            || OpeningDecisionStatus.IsRejected(task.StrDecision?.Status)) {
            return false;
        }

        var approvers = task.Approvers;
        if(approvers is null || approvers.Count == 0) {
            return OpeningDecisionStatus.IsApproved(task.Status);
        }

        return approvers.All(discipline => OpeningDecisionStatus.IsApproved(GetDecision(task, discipline)?.Status));
    }

    /// <summary>
    /// Кому задание адресовано, по-русски - для колонки навигатора. Пусто, если сервер состав
    /// согласующих не вернул.
    /// </summary>
    public static string GetApproversText(OpeningTaskViewDto task) {
        var approvers = task?.Approvers;
        return approvers is null || approvers.Count == 0
            ? string.Empty
            : string.Join(" + ", approvers);
    }

    /// <summary>
    /// Требуется ли решение этой дисциплины. Состав согласующих сервер мог не вернуть - тогда
    /// решение требуется от обеих, как было до появления выбора.
    /// </summary>
    public static bool IsRequired(OpeningTaskViewDto task, Discipline discipline) {
        var approvers = task?.Approvers;
        return approvers is null || approvers.Count == 0 || approvers.Contains(discipline);
    }

    private static OpeningDecisionDto GetDecision(OpeningTaskViewDto task, Discipline discipline) {
        return discipline == Discipline.ARC ? task.ArcDecision : task.StrDecision;
    }
}
