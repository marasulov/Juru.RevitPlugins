using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Juru.RevitPlugins.Openings.Models
{
    public class IntersecResult
    {
        public XYZ Point { get; set; }
        public double Thickness { get; set; }
        public Reference SourceReference { get; set; }

        public IntersecResult(XYZ point, double thickness, Reference sourceReference)
        {
            Point = point;
            Thickness = thickness;
            SourceReference = sourceReference;
        }
    }
}
