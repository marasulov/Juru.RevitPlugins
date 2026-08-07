using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace Juru.RevitPlugins.Openings.Services;

public class XYZComparer : IEqualityComparer<XYZ>
{
    public bool Equals(XYZ pt1, XYZ pt2) => pt1.IsAlmostEqualTo(pt2);


    public int GetHashCode(XYZ obj)
    {
        return (int)obj.X ^ (int)obj.Y ^ (int)obj.Z;
    }
}