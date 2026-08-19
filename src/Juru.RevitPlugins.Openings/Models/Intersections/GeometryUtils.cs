using System.Collections.Generic;

using Autodesk.Revit.DB;

namespace Juru.RevitPlugins.Openings.Models.Intersections;

/// <summary>
/// Работа с солидами элементов Revit.
/// </summary>
public static class GeometryUtils {
    private const double _minSolidVolume = 1e-9;

    /// <summary>
    /// Возвращает солиды элемента, включая солиды вложенных геометрических экземпляров.
    /// </summary>
    public static List<Solid> GetSolids(Element element) {
        List<Solid> solids = [];
        var geometry = element.get_Geometry(new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Fine });
        if(geometry is null) {
            return solids;
        }

        Collect(geometry, solids);
        return solids;
    }

    /// <summary>
    /// Возвращает габаритный бокс солида в виде <see cref="Outline"/> для фильтра по габаритам.
    /// </summary>
    public static Outline GetOutline(Solid solid) {
        var box = solid.GetBoundingBox();
        var transform = box.Transform;
        return new Outline(transform.OfPoint(box.Min), transform.OfPoint(box.Max));
    }

    private static void Collect(GeometryElement geometry, List<Solid> solids) {
        foreach(var geometryObject in geometry) {
            switch(geometryObject) {
                case Solid solid when solid.Volume > _minSolidVolume:
                    solids.Add(solid);
                    break;
                case GeometryInstance instance:
                    Collect(instance.GetInstanceGeometry(), solids);
                    break;
            }
        }
    }
}
