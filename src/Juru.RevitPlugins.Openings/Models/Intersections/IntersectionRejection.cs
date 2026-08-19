namespace Juru.RevitPlugins.Openings.Models.Intersections;

/// <summary>
/// Пересечение, для которого задание не выдаётся, и причина отказа.
/// </summary>
public class IntersectionRejection {
    public IntersectionRejection(MepStructureIntersection intersection, string reason) {
        Intersection = intersection;
        Reason = reason;
    }

    /// <summary>
    /// Отклонённое пересечение.
    /// </summary>
    public MepStructureIntersection Intersection { get; }

    /// <summary>
    /// Причина отказа, которую видит пользователь в отчёте.
    /// </summary>
    public string Reason { get; }
}
