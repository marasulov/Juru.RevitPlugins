using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Juru.RevitPlugins.Openings.Models
{


    public record HoleFamilyModel
    {
        private readonly UIDocument _uidoc;

        //public HoleFamilyModel(UIDocument uidoc, XYZ intersectionPoint, Element intersectingElement)
        //{
        //    _uidoc = uidoc;

        //    IntersectionPoint = intersectionPoint;
        //    IntersectingElement = intersectingElement;
        //}


        /// <summary>
        /// Информация для семейства отверстия
        /// </summary>
        /// <param name="uidoc"></param>
        /// <param name="intersectionPoint"></param>
        /// <param name="intersectingElement"></param>
        /// <param name="normal"></param>
        /// <param name="intersectingElementName"></param>
        /// <param name="intersectingElementType"></param>
        /// <param name="intersectingElementTypeSize"></param>
        /// <param name="sourceType"></param>
        /// <param name="sourceThickness"></param>
        /// <param name="isHoleRectangled"></param>
        /// <param name="holeOffset"></param>
        /// <param name="isInsert"></param>
        /// <param name="intersectingElementHeight"></param>
        /// <param name="sourceName"></param>
        /// <param name="intersectingElementWidth"></param>
        public HoleFamilyModel(
            XYZ intersectionPoint,
            Element intersectingElement,
            XYZ normal,
            string sourceName,
            string sourceType,
            string intersectingElementName,
            string intersectingElementType,
            double intersectingElementTypeSize,
            double sourceThickness,
            bool isHoleRectangled,
            double holeOffset,
            bool isInsert,
            double intersectingElementHeight,
            double intersectingElementWidth = 0)
        {
            IntersectionPoint = intersectionPoint;
            IntersectingElement = intersectingElement;
            Normal = normal;
            IntersectingElementName = intersectingElementName;
            IntersectingElementType = intersectingElementType;
            IntersectingElementTypeSize = intersectingElementTypeSize;
            IntersectingElementHeight = intersectingElementHeight;
            IntersectingElementWidth = intersectingElementWidth;
            SourceType = sourceType;
            SourceName = sourceName;
            SourceThickness = sourceThickness;
            IsHoleRectangled = isHoleRectangled;
            HoleOffset = holeOffset;
            IsInsert = isInsert;
        }

        public string SourceName { get; set; }

        /// <summary>
        /// Точка пересечения
        /// </summary>
        public XYZ IntersectionPoint { get; set; }

        /// <summary>
        /// Пересекающийся элемент
        /// </summary>
        public Element IntersectingElement { get; set; }

        public XYZ Normal { get; }

        /// <summary>
        /// Название пересекающего элемента
        /// </summary>
        public string IntersectingElementName { get; set; }

        /// <summary>
        /// Тип пересекающего элемента
        /// </summary>
        public string IntersectingElementType { get; set; }

        /// <summary>
        /// Размер пересекающего элемента
        /// </summary>
        public double IntersectingElementTypeSize { get; set; }

        public double IntersectingElementHeight { get; }
        public double IntersectingElementWidth { get; }

        /// <summary>
        /// Тип материала пересеченной основы
        /// </summary>
        public string SourceType { get; set; }

        public double SourceThickness { get; set; }

        /// <summary>
        /// Форма проема
        /// </summary>
        public bool IsHoleRectangled { get; set; }

        /// <summary>
        /// Смещение
        /// </summary>
        public double HoleOffset { get; set; }

        /// <summary>
        /// Вставка
        /// </summary>
        public bool IsInsert { get; set; }
    }

}
