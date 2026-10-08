using System.Linq;

using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;

namespace Juru.RevitPlugins.RevitTests.Infrastructure;

/// <summary>
/// Элементы модели для тестов. Размеры - в миллиметрах, как их задаёт инженер.
/// Все методы работают внутри открытой транзакции.
/// </summary>
public static class ModelBuilder {
    public static double Mm(double millimeters) {
        return UnitUtils.ConvertToInternalUnits(millimeters, UnitTypeId.Millimeters);
    }

    public static double ToMm(double feet) {
        return UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);
    }

    public static XYZ PointMm(double x, double y, double z) {
        return new XYZ(Mm(x), Mm(y), Mm(z));
    }

    public static Level LowestLevel(Document document) {
        return new FilteredElementCollector(document)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(level => level.Elevation)
            .First();
    }

    /// <summary>
    /// Прямая стена первого базового типа шаблона, от уровня вверх.
    /// </summary>
    public static Wall Wall(Document document, XYZ start, XYZ end, double heightMm = 3000) {
        var wallType = new FilteredElementCollector(document)
            .OfClass(typeof(WallType))
            .Cast<WallType>()
            .First(type => type.Kind == WallKind.Basic);

        return Autodesk.Revit.DB.Wall.Create(
            document, Line.CreateBound(start, end), wallType.Id, LowestLevel(document).Id,
            Mm(heightMm), 0, false, false);
    }

    /// <summary>
    /// Прямоугольный воздуховод между точками в координатах модели.
    /// </summary>
    public static Duct RectangularDuct(Document document, XYZ start, XYZ end, double widthMm, double heightMm) {
        var systemType = new FilteredElementCollector(document)
            .OfClass(typeof(MechanicalSystemType))
            .First();
        var ductType = new FilteredElementCollector(document)
            .OfClass(typeof(DuctType))
            .Cast<DuctType>()
            .First(type => type.Shape == ConnectorProfileType.Rectangular);

        var duct = Duct.Create(document, systemType.Id, ductType.Id, LowestLevel(document).Id, start, end);
        duct.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM).Set(Mm(widthMm));
        duct.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM).Set(Mm(heightMm));
        return duct;
    }
}
