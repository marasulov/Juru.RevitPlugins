using Autodesk.Revit.DB;

using Juru.RevitPlugins.Openings.Models.Server.Dto;

namespace Juru.RevitPlugins.Openings.Models.Server;

/// <summary>
/// Читает отпечаток общих координат документа - для сервера, который сравнивает его с прошлым
/// батчем той же дисциплины и объекта (см. "Fingerprint координат" в <c>docs/OpeningsApi.md</c>).
/// </summary>
public static class CoordinateFingerprintReader {
    /// <summary>
    /// Возвращает <c>SharedPosition</c> точки съёмки и базовой точки проекта в метрах -
    /// то же самое, что проверяет <see cref="ProjectCoordinatesCheck"/> на "Acquire не сделан",
    /// только числами для сравнения между отправками, а не булевым ответом.
    /// </summary>
    public static OpeningCoordinateFingerprintDto Get(Document doc) {
        var survey = BasePoint.GetSurveyPoint(doc).SharedPosition;
        var project = BasePoint.GetProjectBasePoint(doc).SharedPosition;

        return new OpeningCoordinateFingerprintDto {
            SurveyX = ToMeters(survey.X),
            SurveyY = ToMeters(survey.Y),
            SurveyZ = ToMeters(survey.Z),
            ProjectX = ToMeters(project.X),
            ProjectY = ToMeters(project.Y),
            ProjectZ = ToMeters(project.Z)
        };
    }

    private static double ToMeters(double feet) {
        return UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Meters);
    }
}
