using Autodesk.Revit.DB;

using Juru.RevitPlugins.Openings.Models.Configs;

namespace Juru.RevitPlugins.Openings.Models.Placement;

/// <summary>
/// Габариты и положение задания на отверстие в единицах Revit.
/// </summary>
public class OpeningSize {
    public OpeningSize(
        OpeningType openingType,
        double width,
        double height,
        double diameter,
        double thickness,
        XYZ center) {

        OpeningType = openingType;
        Width = width;
        Height = height;
        Diameter = diameter;
        Thickness = thickness;
        Center = center;
    }

    /// <summary>
    /// Тип отверстия: конструкция и форма.
    /// </summary>
    public OpeningType OpeningType { get; }

    /// <summary>
    /// Ширина прямоугольного отверстия.
    /// </summary>
    public double Width { get; }

    /// <summary>
    /// Высота прямоугольного отверстия.
    /// </summary>
    public double Height { get; }

    /// <summary>
    /// Диаметр круглого отверстия.
    /// </summary>
    public double Diameter { get; }

    /// <summary>
    /// Глубина отверстия - толщина конструкции в месте прохода. Не округляется.
    /// </summary>
    public double Thickness { get; }

    /// <summary>
    /// Геометрический центр отверстия в координатах активного файла.
    /// </summary>
    public XYZ Center { get; }

    /// <summary>
    /// Круглое ли отверстие.
    /// </summary>
    public bool IsRound => OpeningType is OpeningType.WallRound or OpeningType.FloorRound;

    /// <summary>
    /// Объём отверстия в единицах Revit - по габаритному боксу, а не по форме:
    /// он нужен только для сравнения заданий между собой.
    /// </summary>
    public double BoxVolume => IsRound
        ? Diameter * Diameter * Thickness
        : Width * Height * Thickness;
}
