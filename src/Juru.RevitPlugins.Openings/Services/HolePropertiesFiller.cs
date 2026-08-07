using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using System.Collections.Generic;
using Juru.RevitPlugins.Openings.Models;


namespace Juru.RevitPlugins.Openings.Services
{
    public class HolePropertiesFiller
    {
        private readonly Document _doc;

        public HolePropertiesFiller(Document doc, Element intersectingElement, Reference intersectedSourceElement, XYZ intersectionPoint)
        {
            _doc = doc;
            IntersectingElement = intersectingElement;
            IntersectedSourceElement = intersectedSourceElement;
            IntersectionPoint = intersectionPoint;
        }

        public Element IntersectingElement { get; set; }
        public Reference IntersectedSourceElement { get; set; }
        public XYZ IntersectionPoint { get; set; }
        public List<HoleFamilyModel> HolesProps { get; set; } = new();

        public void GetHoles(XYZ point, UIDocument uidoc, XYZ normal)
        {
            int i = 0;

            if (IntersectedSourceElement == null) return;

            if (_doc.GetElement(IntersectedSourceElement.ElementId) is not RevitLinkInstance link) return;

            var ldoc = link.GetLinkDocument();

            Element el = ldoc.GetElement(IntersectedSourceElement.LinkedElementId);
            Element sourceElement = el.GetType().Name switch
            {
                "Wall" => el as Wall,
                "Floor" => el as Floor,
                _ => default
            };

            if (sourceElement == null) return;
            var holeFamily = GetHoleProps(point, sourceElement, IntersectingElement, normal);
            HolesProps.Add(holeFamily);
        }

        public HoleFamilyModel GetHoleProps(XYZ intPoint, Element sourceElement, Element element, XYZ normal)
        {

            var elType = element.GetType();
            double pipeSize = default;
            string elName = default;

            double width = default;
            double height = default;
            switch (elType.Name)
            {
                //TODO 
                case "Duct":
                    {
                        var intersectElement = element as Duct;
                        elName = intersectElement.MEPSystem.Name;
                        if (intersectElement.DuctType.Shape == ConnectorProfileType.Round)
                        {
                            width = intersectElement.Diameter;
                            height = width;
                        }
                        else
                        {
                            width = intersectElement.Width;
                            height = intersectElement.Height;
                        }
                        
                        //pipeSize = intersectElement.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE).AsDouble();
                        break;
                    }
                case "Pipe":
                    {
                        var intersectElement = element as Pipe;
                        elName = intersectElement.PipeType.Name;
                        pipeSize = intersectElement.get_Parameter(BuiltInParameter.RBS_PIPE_OUTER_DIAMETER).AsDouble();
                        break;
                    }
            }

            var sourceType = sourceElement.GetType();
            double sourceElementThickness = default;
            switch (sourceType.Name)
            {
                case "Wall":
                    {
                        sourceElementThickness = (sourceElement as Wall).Width;
                        break;
                    }
                case "Floor":
                    {
                        sourceElementThickness = (sourceElement as Floor).get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM).AsDouble();
                        break;
                    }
            }

            var holeFamilyModel = new HoleFamilyModel(intPoint, element, normal, sourceElement.Name, sourceType.Name, elName, elType.Name, pipeSize,
                sourceElementThickness, true, 20, true, height, width);

            return holeFamilyModel;
        }
    }
}
