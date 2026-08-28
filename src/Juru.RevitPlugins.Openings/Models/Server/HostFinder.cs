using System.Linq;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Openings.Models.Configs;

namespace Juru.RevitPlugins.Openings.Models.Server;

/// <summary>
/// Находит хост размещённого входящего задания геометрией - точкой вставки, а не связью:
/// в файле АР/КР задание не хостовое (свободное семейство на уровне, docs/OpeningTaskFamilies.md),
/// а реальная стена или перекрытие лежат в этом же документе локально.
/// </summary>
public static class HostFinder {
    /// <summary>
    /// Допуск поиска вокруг точки вставки в мм - тот же приём, что и при расстановке заданий:
    /// точка пересечения может оказаться чуть внутри конструкции, а не точно на её грани.
    /// </summary>
    private const double _toleranceMm = 100;

    public static Element? Find(Document doc, XYZ point, OpeningType openingType) {
        var category = openingType is OpeningType.WallRectangle or OpeningType.WallRound
            ? BuiltInCategory.OST_Walls
            : BuiltInCategory.OST_Floors;

        double tolerance = UnitUtils.ConvertToInternalUnits(_toleranceMm, UnitTypeId.Millimeters);
        var outline = new Outline(
            new XYZ(point.X - tolerance, point.Y - tolerance, point.Z - tolerance),
            new XYZ(point.X + tolerance, point.Y + tolerance, point.Z + tolerance));

        return new FilteredElementCollector(doc)
            .OfCategory(category)
            .WhereElementIsNotElementType()
            .WherePasses(new BoundingBoxIntersectsFilter(outline))
            .FirstOrDefault();
    }
}
