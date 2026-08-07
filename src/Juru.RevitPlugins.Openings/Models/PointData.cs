using Autodesk.Revit.DB;

namespace Juru.RevitPlugins.Openings.Models
{
    public class PointData
    {
        public ElementId Id { get; set; }
        public XYZ Point { get; set; }

        public PointData(ElementId id, XYZ point)
        {
            Id = id;
            Point = point;
        }
    }
}
