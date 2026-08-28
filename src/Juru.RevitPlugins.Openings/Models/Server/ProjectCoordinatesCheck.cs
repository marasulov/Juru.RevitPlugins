using Autodesk.Revit.DB;

namespace Juru.RevitPlugins.Openings.Models.Server;

/// <summary>
/// Проверяет, что в документе действительно установлены общие координаты, прежде чем координаты
/// задания уйдут на сервер относительно базовой точки проекта - см. "Система координат" в
/// <c>docs/OpeningsApi.md</c>.
/// </summary>
public static class ProjectCoordinatesCheck {
    /// <summary>
    /// True, если точка съёмки или базовая точка проекта различают внутренние и общие координаты -
    /// то есть <c>Acquire Coordinates</c> хоть раз выполнялся в этом файле. Не доказывает, что
    /// координаты согласованы правильно с эталоном - только что они не остались нетронутыми
    /// по умолчанию, когда обе координаты совпадают.
    /// </summary>
    public static bool HasAcquiredCoordinates(Document doc) {
        var survey = BasePoint.GetSurveyPoint(doc);
        var project = BasePoint.GetProjectBasePoint(doc);
        return !survey.Position.IsAlmostEqualTo(survey.SharedPosition)
            || !project.Position.IsAlmostEqualTo(project.SharedPosition);
    }
}
