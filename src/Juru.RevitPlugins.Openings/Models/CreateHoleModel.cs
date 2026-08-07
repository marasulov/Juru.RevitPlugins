using Autodesk.Revit.UI;

namespace Juru.RevitPlugins.Openings.Models
{
    public class CreateHoleModel //: ICreateHoleModel
    {
        private readonly UIDocument _uiDocument;
        //private readonly RevitRepository _revitRepository;

        //public CreateHoleModel(RevitRepository revitRepository)
        //{
        //    _revitRepository = revitRepository;
        //}

        public CreateHoleModel(UIDocument uiDocument)
        {
            _uiDocument = uiDocument;
        }

        //public Document Document => _revitRepository.Document;

        //public void CreateHole()
        //{
        //    var doc = _uiDocument.Document;
        //    var offset = 1.1;
        //    double pipeDiametrForFilter = 50;
        //    var settingsReader = new SettingsReader();
        //    var names = settingsReader.GetFamilyNames();


        //    //var recFamilyNames = settingsReader.GetFamilyTypes().FamilyTypes.Rectangled.FamilyNames;
        //    //var circledFamilyNames = settingsReader.GetFamilyTypes().FamilyTypes.Circled.FamilyNames;
        //    //var linkedDocs = GetLinkedDocuments(doc);

        //    var familyNames = new HashSet<string>(names);
        //    var familyInstances = new FilteredElementCollector(doc)
        //        .WhereElementIsNotElementType()
        //        .OfClass(typeof(FamilyInstance))
        //        .Cast<FamilyInstance>()
        //        .Where(fi => familyNames.Contains(
        //            fi.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?
        //                .AsValueString()))
        //        .ToArray();

        //    using var transactionGroup = new TransactionGroup(doc);

        //    var collector3dView = new FilteredElementCollector(doc);
        //    Func<View3D, bool> isNotTemplate = v3 => !(v3.IsTemplate);
        //    View3D view3D = collector3dView
        //        .OfClass(typeof(View3D))
        //        .Cast<View3D>()
        //        .First(isNotTemplate);

        //    List<PointData> pointDatas = familyInstances.Select(familyInstance => new PointData(familyInstance.Id, (familyInstance.Location as LocationPoint).Point)).ToList();

        //    //var familyPoints = familyInstances.Where(w => w.Location is LocationPoint)
        //    //    .Select(f => f.Location as LocationPoint)
        //    //    .Select(loc => loc.Point).ToList();

        //    var smallestDiametr = UnitUtils.ConvertToInternalUnits(pipeDiametrForFilter, UnitTypeId.Millimeters);
        //    var pipeCollector = new FilteredElementCollector(doc).OfClass(typeof(Pipe)).Cast<Pipe>()
        //        .Where(w => w.Diameter > smallestDiametr);

        //    var ductCollector = new FilteredElementCollector(doc).OfClass(typeof(Duct)).Cast<Duct>()
        //        .Where(w => w.Width > smallestDiametr);

        //    List<HoleFamilyModel> wallHoles = new List<HoleFamilyModel>();

        //    //исходные точки
        //    var newIntersections = new List<XYZ>();

        //    //из коллекции труб создаем отверстия
        //    foreach (Element pipeElement in pipeCollector)
        //    {
        //        ReferenceIntersectionFinder refFinder = new ReferenceIntersectionFinder(doc, pipeElement, view3D);

        //        refFinder.GetStructuralReferences(BuiltInCategory.OST_Walls);

        //        if (refFinder.WallReferences.Count > 0)
        //        {
        //            var points = refFinder.GetIntersectionsPoints(refFinder.WallReferences);

        //            newIntersections.AddRange(points);
        //            int i = 0;

        //            var holes = new List<HoleFamilyModel>();
        //            foreach (var point in points)
        //            {
        //                var sourceElement = refFinder.WallReferences[i];
        //                if (sourceElement == null) continue;
        //                var holeFiller = new HolePropertiesFiller(doc, pipeElement, sourceElement, point);
        //                holeFiller.GetHoles(point, _uiDocument, refFinder.Normal);
        //                holes = holeFiller.HolesProps;
        //            }
        //            wallHoles.AddRange(holes);
        //        }

        //        refFinder.GetStructuralReferences(BuiltInCategory.OST_Floors);
        //        if (refFinder.FloorReferences.Count > 0)
        //        {
        //            newIntersections.AddRange(refFinder.GetIntersectionsPoints(refFinder.FloorReferences));
        //        }
        //    }

        //    List<ActualHoleModelDto> actualHoles = new();
        //    List<NewHolesDto> newHoles = new();

        //    var outdatedFamilies = new List<OutdatedFamilyDto>();

        //    // если есть семейства значит не первый раз вставляются семейства отверстий

        //    if (pointDatas.Count > 0)
        //    {
        //        var familyPoints = pointDatas.Select(x=>x.Point);
        //        var actualPoints = pointDatas.Where(pd => newIntersections.Contains(pd.Point));

        //        //var actualPoints = newIntersections.Intersect(familyPoints, new XYZComparer()).ToArray();

        //        var newPoints = newIntersections.Except(familyPoints, new XYZComparer()).ToArray(); // newIntersections.Except(familyPoints)
        //        var deletedPoints = familyPoints.Except(newIntersections, new XYZComparer()).ToArray(); // familyPoints.Except(newIntersections)
        //        var typeOfSource = "Wall";

        //        foreach (var actualPoint in actualPoints)
        //        {
        //            var actualModels = wallHoles.Where(holeFamilyModel =>
        //                actualPoint.Point == holeFamilyModel.IntersectionPoint);

        //            actualHoles.AddRange(actualModels.Select(actualModel =>
        //                new ActualHoleModelDto(
        //                    actualPoint.Id.IntegerValue,
        //                    new PointDTO(actualModel.IntersectionPoint.X, actualModel.IntersectionPoint.Y, actualModel.IntersectionPoint.Z),
        //                    actualModel.IntersectingElementName,
        //                    actualModel.SourceType,
        //                    actualModel.SourceType,
        //                    actualModel.IntersectingElementTypeSize,
        //                    actualModel.SourceType,
        //                    true,
        //                    UnitUtils.ConvertToInternalUnits(20, UnitTypeId.Millimeters),
        //                    false,
        //                    actualModel.SourceThickness,
        //                    new PointDTO(actualModel.IntersectionPoint.X, actualModel.IntersectionPoint.Y, actualModel.IntersectionPoint.Z),
        //                    actualModel.IntersectingElementWidth
        //                )));
        //        }

        //        var i = 1;
        //        foreach (var newPoint in newPoints)
        //        {
        //            var newModels = wallHoles.Where(holeFamilyModel =>
        //                newPoint == holeFamilyModel.IntersectionPoint);

        //            newHoles.AddRange(
        //                newModels.Select(actualModel =>
        //                    new NewHolesDto(
        //                        actualModel.IntersectingElement.Id.IntegerValue,
        //                        new PointDTO(actualModel.IntersectionPoint.X, actualModel.IntersectionPoint.Y, actualModel.IntersectionPoint.Z),
        //                        actualModel.IntersectingElementName,
        //                        actualModel.SourceType,
        //                        actualModel.SourceType,
        //                        actualModel.IntersectingElementTypeSize,
        //                        actualModel.SourceType,
        //                        true,
        //                        UnitUtils.ConvertToInternalUnits(20, UnitTypeId.Millimeters),
        //                        true,
        //                        actualModel.SourceThickness,
        //                        new PointDTO(actualModel.IntersectionPoint.X, actualModel.IntersectionPoint.Y, actualModel.IntersectionPoint.Z),
        //                        actualModel.IntersectingElementWidth
        //                        )));
        //            i++;
        //        }

        //        //foreach (var deletedPoint in deletedPoints)
        //        //{
        //        //    foreach (var family in familyInstances)
        //        //    {
        //        //        var loc = family.Location as LocationPoint;

        //        //        if (deletedPoint.IsAlmostEqualTo(loc?.Point))
        //        //        {
        //        //            var familyName = family.Symbol.Family.Name;
        //        //            outdatedFamilies.Add(new OutdatedFamilyDto(family.Id.IntegerValue, familyName));
        //        //        }
        //        //    }
        //        //}
        //    }
        //    else
        //    {
        //        var j = 0;
        //        foreach (var hole in wallHoles)
        //        {
        //            var holeModel = new ActualHoleModelDto(
        //                j,
        //                new PointDTO(hole.IntersectionPoint.X, hole.IntersectionPoint.Y, hole.IntersectionPoint.Z),
        //                hole.IntersectingElementName,
        //                hole.SourceType,
        //                hole.IntersectingElementType,
        //                hole.IntersectingElementTypeSize,
        //                "", hole.IsHoleRectangled, hole.HoleOffset, hole.IsDelete, hole.SourceThickness,
        //            new PointDTO(hole.IntersectionPoint.X, hole.IntersectionPoint.Y, hole.IntersectionPoint.Z),
        //                hole.IntersectingElementWidth
        //                );

        //            actualHoles.Add(holeModel);
        //            j++;
        //        }
        //    }

        //    Debug.Print($"{newIntersections.Count} точек {wallHoles.Count} отверстий");


        //   


    }
}
