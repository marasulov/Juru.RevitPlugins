using System;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Openings.Models.Configs;

namespace Juru.RevitPlugins.Openings.Models.Placement;

/// <summary>
/// Точка вставки задания на отверстие.
/// </summary>
/// <remarks>
/// Семейства взяты из библиотеки Bim4Everyone, поэтому точка вставки у них не одна и та же:
/// у круглого отверстия в стене она в геометрическом центре, у прямоугольного в стене - у нижней
/// грани, а у отверстий в перекрытии - у верхней грани плиты, от которой считаются отметки на
/// чертежах. Здесь эти конвенции собраны в одном месте, чтобы не разъезжаться по расчётам.
/// </remarks>
public static class InsertionPoint {
    /// <summary>
    /// Возвращает точку вставки семейства по габаритам отверстия.
    /// </summary>
    public static XYZ Get(OpeningSize size) {
        if(size is null) { throw new ArgumentNullException(nameof(size)); }

        switch(size.OpeningType) {
            case OpeningType.WallRound:
                return size.Center;
            case OpeningType.WallRectangle:
                return size.Center - XYZ.BasisZ * size.Height / 2;
            case OpeningType.FloorRectangle:
            case OpeningType.FloorRound:
                return size.Center + XYZ.BasisZ * size.Thickness / 2;
            default:
                throw new ArgumentOutOfRangeException(nameof(size));
        }
    }
}
