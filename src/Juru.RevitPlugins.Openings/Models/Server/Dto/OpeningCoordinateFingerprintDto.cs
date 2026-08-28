namespace Juru.RevitPlugins.Openings.Models.Server.Dto;

/// <summary>
/// Отпечаток общих координат файла-отправителя на момент отправки - позиция точки съёмки и базовой
/// точки проекта в их собственных внутренних координатах (<c>SharedPosition</c>), в метрах.
/// Сервер сравнивает его с отпечатком, сохранённым от прошлого батча той же дисциплины и объекта,
/// чтобы заметить, что общие координаты в файле-отправителе сместились - см. "Fingerprint координат"
/// в <c>docs/OpeningsApi.md</c>.
/// </summary>
public class OpeningCoordinateFingerprintDto {
    public double SurveyX { get; set; }
    public double SurveyY { get; set; }
    public double SurveyZ { get; set; }
    public double ProjectX { get; set; }
    public double ProjectY { get; set; }
    public double ProjectZ { get; set; }
}
