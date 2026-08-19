using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;

namespace Juru.RevitPlugins.Openings.Models.Intersections;

/// <summary>
/// Габариты сечения инженерного элемента в единицах Revit.
/// </summary>
public class MepElementSize {
    private MepElementSize(bool isRound, double diameter, double width, double height) {
        IsRound = isRound;
        Diameter = diameter;
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Круглое ли сечение элемента.
    /// </summary>
    public bool IsRound { get; }

    /// <summary>
    /// Диаметр круглого сечения, либо 0 для прямоугольного.
    /// </summary>
    public double Diameter { get; }

    /// <summary>
    /// Ширина прямоугольного сечения, либо 0 для круглого.
    /// </summary>
    public double Width { get; }

    /// <summary>
    /// Высота прямоугольного сечения, либо 0 для круглого.
    /// </summary>
    public double Height { get; }

    /// <summary>
    /// Габарит, по которому решается, нужно ли отверстие: диаметр либо большая сторона сечения.
    /// </summary>
    public double GoverningSize => IsRound ? Diameter : (Width > Height ? Width : Height);

    /// <summary>
    /// Читает габариты сечения элемента, либо возвращает <c>null</c>, если у элемента нет сечения
    /// (например, у соединительной арматуры).
    /// </summary>
    public static MepElementSize GetSize(Element element) {
        switch(element) {
            case Pipe pipe:
                return new MepElementSize(true, pipe.Diameter, 0, 0);
            case Conduit conduit:
                return new MepElementSize(true, conduit.Diameter, 0, 0);
            case Duct duct:
                return duct.DuctType?.Shape == ConnectorProfileType.Round
                    ? new MepElementSize(true, duct.Diameter, 0, 0)
                    : new MepElementSize(false, 0, duct.Width, duct.Height);
            case CableTray tray:
                return new MepElementSize(false, 0, tray.Width, tray.Height);
            case FamilyInstance fitting:
                return GetFittingSize(fitting);
            default:
                return GetSizeFromParameters(element);
        }
    }

    /// <summary>
    /// Читает габариты по встроенным параметрам сечения.
    /// Нужно для элементов, у которых нет типизированного API габаритов.
    /// </summary>
    private static MepElementSize GetSizeFromParameters(Element element) {
        double diameter = GetLength(element, BuiltInParameter.RBS_CURVE_DIAMETER_PARAM);
        if(diameter > 0) {
            return new MepElementSize(true, diameter, 0, 0);
        }

        double width = GetLength(element, BuiltInParameter.RBS_CURVE_WIDTH_PARAM);
        double height = GetLength(element, BuiltInParameter.RBS_CURVE_HEIGHT_PARAM);
        return width > 0 || height > 0 ? new MepElementSize(false, 0, width, height) : null;
    }

    /// <summary>
    /// Читает габариты соединительной арматуры по её коннекторам.
    /// У отвода или тройника нет собственного сечения, поэтому берётся наибольший габарит
    /// среди присоединяемых элементов.
    /// </summary>
    private static MepElementSize GetFittingSize(FamilyInstance fitting) {
        var connectors = fitting.MEPModel?.ConnectorManager?.Connectors;
        if(connectors is null) {
            return null;
        }

        double diameter = 0;
        double width = 0;
        double height = 0;
        foreach(Connector connector in connectors) {
            switch(connector.Shape) {
                case ConnectorProfileType.Round:
                    if(connector.Radius * 2 > diameter) {
                        diameter = connector.Radius * 2;
                    }
                    break;
                case ConnectorProfileType.Rectangular:
                case ConnectorProfileType.Oval:
                    if(connector.Width > width) {
                        width = connector.Width;
                    }
                    if(connector.Height > height) {
                        height = connector.Height;
                    }
                    break;
            }
        }

        if(diameter > 0 && diameter >= width && diameter >= height) {
            return new MepElementSize(true, diameter, 0, 0);
        }
        return width > 0 || height > 0 ? new MepElementSize(false, 0, width, height) : null;
    }

    private static double GetLength(Element element, BuiltInParameter parameter) {
        var value = element.get_Parameter(parameter);
        return value is null || !value.HasValue ? 0 : value.AsDouble();
    }
}
