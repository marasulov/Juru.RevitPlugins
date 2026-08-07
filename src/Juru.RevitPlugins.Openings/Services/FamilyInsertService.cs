using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using Juru.RevitPlugins.Openings.Contracts;
using Juru.RevitPlugins.Openings.Contracts.DTOs;
using Juru.RevitPlugins.Openings.Services;

namespace Juru.RevitPlugins.Openings.Services
{
    public class FamilyInsertService : IFamilyInsertService
    {
        private readonly Document _doc;

        //private readonly List<string> _familyParameters;

        public FamilyInsertService(UIDocument uiDocument)
        {
            _doc = uiDocument.Document;
        }

        public void InsertFamily(AllHolesDto allHoles)
        {

            var newHoles = allHoles.NewFamiliesDtos.Where(i => i.IsInsert);
            var actualHoles = allHoles.ActualFamiliesDtos.Where(i => i.IsDelete).ToArray();
            var outdatedHoles = allHoles.OutdatedFamiliesDtos.Where(i => i.IsDelete);

            using var t = new Transaction(_doc);
            t.Start("Hole task for walls and floors with pipes");

            foreach (var hole in newHoles)
            {
                FamilyTypeFinderService familyTypeFinderService = new FamilyTypeFinderService();

                var wallsFamilyName = hole.SourceType switch
                {
                    "Wall" => familyTypeFinderService.GetFamilyType(BuiltInCategory.OST_Walls, hole.Shape),
                    "Floor" => familyTypeFinderService.GetFamilyType(BuiltInCategory.OST_Floors, hole.Shape),
                    _ => default
                };

                //var wallsFamilyType = familyTypeFinderService.FamilyParameters;

                FamilySymbol symbol = familyTypeFinderService.GetFamilySymbolToPlace(_doc, wallsFamilyName);

                if (symbol == null)
                {
                    break;
                }
                //FamilyInsertService insertService = new FamilyInsertService(_doc, symbol);
                //Reference r = new Reference(pipeElement);
                //ReferenceIntersectionFinder refFinder = new ReferenceIntersectionFinder(doc, pipeElement, view3D);


                InsertFamily(symbol, hole);
            }

            var actualHolesId = actualHoles.Select(x => new ElementId(x.Id)).ToArray();

            foreach (var id in actualHolesId)
            {
                _doc.Delete(id);
            }

            var outdatedHolesids = outdatedHoles.Select(x => x.Id).ToArray();


            foreach (var id in outdatedHolesids)
            {
                var i = int.Parse(id);
                _doc.Delete(new ElementId(i));
            }

            t.Commit();
        }

        public void InsertFamily(FamilySymbol symbol, NewHolesDto dto)
        {
            var point = new XYZ(dto.IntersectionPoint.X, dto.IntersectionPoint.Y, dto.IntersectionPoint.Z);
            if (!symbol.IsActive) symbol.Activate();
            FamilyInstance fi = _doc.Create.NewFamilyInstance(point, symbol, StructuralType.NonStructural);
            var basisY = fi.GetTransform().BasisY;
            var angle = basisY.AngleTo(new XYZ(dto.IntersectionNormal.X, dto.IntersectionNormal.Y, dto.IntersectionNormal.Z));

            Line axis = Line.CreateBound(point, point + XYZ.BasisZ);
            ElementTransformUtils.RotateElement(_doc, fi.Id, axis, -angle);
            var parameters = fi.GetOrderedParameters();

            var offset = UnitUtils.ConvertToInternalUnits(dto.HoleOffset, UnitTypeId.Millimeters);
            //var height = UnitUtils.ConvertToInternalUnits(dto.Height, UnitTypeId.Millimeters);
            //var width = UnitUtils.ConvertToInternalUnits(dto.Width, UnitTypeId.Millimeters);
            var intersectingElementSize = UnitUtils.ConvertToInternalUnits(dto.IntersectingElementTypeSize, UnitTypeId.Millimeters);

            switch (dto.Shape)
            {
                case "Circle" when dto.IntersectingElementType == "Duct":
                    var diameter = Math.Sqrt(Math.Pow(dto.Width, 2) + Math.Pow(dto.Height, 2));
                    //if (dto.Width < dto.Height)
                    //    diameter = dto.Height;
                    SetCircledFamilyParameter(parameters, "Depth", "Diameter", diameter, offset, dto.SourceThickness);
                    break;
                case "Circle":
                    SetCircledFamilyParameter(parameters, "Depth", "Diameter", intersectingElementSize, offset, dto.SourceThickness);
                    break;


                case "Square" when dto.IntersectingElementType == "Duct" && dto.Height != 0:
                    SetSquaredFamilyParameter(parameters, "Depth", "Height", "Width", dto.Height, dto.Width,
                        offset, dto.SourceThickness);
                    break;
                case "Square":
                    SetRectFamilyParameter(parameters, "Depth", "Height", "Width", intersectingElementSize, offset, dto.SourceThickness);
                    break;
            }
            //}
        }

        private static void SetCircledFamilyParameter(IList<Parameter> parameters, string familyLength, string familyWidth, double diameter, double offset, double thickness)
        {
            foreach (var parameter in parameters)
            {

                if (parameter.Definition.Name == familyWidth)
                {
                    parameter.Set(diameter + offset * 2);
                }

                if (parameter.Definition.Name != familyLength) continue;
                parameter.Set(thickness + (thickness * offset));

            }
        }

        private static void SetRectFamilyParameter(IList<Parameter> parameters, string familyLength, string familyHeight, string familyWidth, double diameter, double offset, double thickness)
        {
            foreach (var parameter in parameters)
            {

                if (parameter.Definition.Name == familyWidth)
                {
                    parameter.Set(diameter + offset * 2);
                }

                if (parameter.Definition.Name == familyHeight)
                {
                    parameter.Set(diameter + offset * 2);
                }

                if (parameter.Definition.Name != familyLength) continue;
                parameter.Set(thickness + thickness * offset);


            }
        }

        private void SetSquaredFamilyParameter(IList<Parameter> parameters, string familyLength, string familyHeight, string familyWidth, double height, double width, double offset, double thickness)
        {
            foreach (var parameter in parameters)
            {

                if (parameter.Definition.Name == familyWidth)
                {
                    parameter.Set(width + offset * 2);
                }

                if (parameter.Definition.Name == familyHeight)
                {
                    parameter.Set(height + offset * 2);
                }

                if (parameter.Definition.Name != familyLength) continue;
                parameter.Set(thickness + thickness * offset);


            }
        }

        private void DeleteFamily(ICollection<ElementId> deletedElementIds)
        {
            ICollection<ElementId> deletedIdSet = _doc.Delete(deletedElementIds);

            if (0 != deletedIdSet.Count)
            {
                throw new Exception("Deleting the selected elements in Revit failed.");
            }

            TaskDialog.Show("Revit", "The selected element has been removed.");
        }
    }


}
