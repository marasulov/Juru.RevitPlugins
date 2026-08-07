using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Linq;
using Juru.RevitPlugins.Openings.Contracts;
using Juru.RevitPlugins.Openings.Contracts.DTOs;
using Juru.RevitPlugins.Openings.Models;
using Juru.RevitPlugins.Openings.Services;
using Juru.RevitPlugins.Openings.Settings;

namespace Juru.RevitPlugins.Openings.Services
{
    public class FindHolesService : IFindHoleService
    {
        private readonly UIDocument _uiDocument;
        private EntityToDtoConverter _entityToDTOConverter;

        public FindHolesService(UIDocument uiDocument, EntityToDtoConverter entityToDtoConverter)
        {
            _uiDocument = uiDocument;
            _entityToDTOConverter = entityToDtoConverter;
        }

        private IEnumerable<Element> GetMEPElements(Document doc, double minSizeMm)
        {
            double minSizeInternal = UnitUtils.ConvertToInternalUnits(minSizeMm, UnitTypeId.Millimeters);

            // 1. Трубы
            var pipes = new FilteredElementCollector(doc)
                .OfClass(typeof(Pipe))
                .Cast<Pipe>()
                .Where(p => p.Diameter > minSizeInternal)
                .Cast<Element>();

            // 2. Воздуховоды (проверяем и диаметр, и максимальную из сторон для прямоугольных)
            var ducts = new FilteredElementCollector(doc)
                .OfClass(typeof(Duct))
                .Cast<Duct>()
                .Where(d =>
                {
                    if (d.DuctType.Shape == ConnectorProfileType.Round)
                        return d.Diameter > minSizeInternal;
                    else
                        return Math.Max(d.Width, d.Height) > minSizeInternal;
                })
                .Cast<Element>();

            // 3. Лотки (заготовка на будущее)
            // var cableTrays = new FilteredElementCollector(doc).OfClass(typeof(CableTray))...

            // Склеиваем все в один список
            return pipes.Concat(ducts);
        }

        //private void ProcessIntersections(
        //    Document doc,
        //    Element mepElement,
        //    BuiltInCategory hostCategory,
        //    View3D view3D,
        //    List<XYZ> outPoints,
        //    List<HoleFamilyModel> outHolesProps)
        //{
        //    var refFinder = new ReferenceIntersectionFinder(doc, mepElement, view3D);

        //    // Внутри этого метода должен запускаться луч и алгоритм кластеризации (склеивания слоев), 
        //    // который мы написали ранее. Возвращает список несущих основ и их центров.
        //    List<IntersecResult> intersectResults = refFinder.GetIntersections(hostCategory);

        //    foreach (var result in intersectResults)
        //    {
        //        var sourceElement = result.SourceReference;
        //        if (sourceElement == null) continue;

        //        // Добавляем точку в общий список для проверки "Новое/Старое отверстие"
        //        outPoints.Add(result.Point);

        //        // Формируем данные для вставки
        //        var holeFiller = new HolePropertiesFiller(doc, mepElement, sourceElement, result.Point);
        //        holeFiller.GetHoles(result.Point, _uiDocument, refFinder.Normal);

        //        outHolesProps.AddRange(holeFiller.HolesProps);
        //    }
        //}

        private List<HoleFamilyModel> CleanupDuplicateHoles(List<HoleFamilyModel> holes, double toleranceMm)
        {
            // Максимальное расстояние между точками вдоль луча (например, 500 мм для клапана)
            double lengthTolerance = UnitUtils.ConvertToInternalUnits(toleranceMm, UnitTypeId.Millimeters);

            // Максимальное смещение вбок (перпендикулярно лучу). 
            // 50 мм хватит, чтобы простить легкую неточность черчения смежников, 
            // но при этом не склеить две разные трубы.
            double axisTolerance = UnitUtils.ConvertToInternalUnits(50, UnitTypeId.Millimeters);

            List<HoleFamilyModel> cleanedList = new List<HoleFamilyModel>();

            foreach (var currentHole in holes)
            {
                bool isDuplicate = false;

                foreach (var existingHole in cleanedList)
                {
                    // Вектор между двумя центрами отверстий
                    XYZ vecBetweenPoints = currentHole.IntersectionPoint.Subtract(existingHole.IntersectionPoint);
                    double distance = vecBetweenPoints.GetLength();

                    // Если они находятся в пределах общей дистанции (например, 500 мм)
                    if (distance < lengthTolerance)
                    {
                        // Проверяем, лежат ли они на одном луче.
                        // Векторное произведение (CrossProduct) вектора смещения и вектора направления луча
                        // даст нам вектор, длина которого равна ПЕРПЕНДИКУЛЯРНОМУ расстоянию между осями.
                        XYZ crossProduct = vecBetweenPoints.CrossProduct(existingHole.Normal);
                        double perpendicularDistance = crossProduct.GetLength();

                        // Если точки не съехали вбок больше чем на 50 мм — это одна и та же трасса!
                        if (perpendicularDistance < axisTolerance)
                        {
                            isDuplicate = true;
                            break;
                        }
                    }
                }

                // Если это не дубликат с той же оси — добавляем в чистый список
                if (!isDuplicate)
                {
                    cleanedList.Add(currentHole);
                }
            }

            return cleanedList;
        }

        private void ProcessIntersections(
            Document doc,
            Element mepElement,
            BuiltInCategory hostCategory,
            View3D view3D,
            List<XYZ> outPoints,
            List<HoleFamilyModel> outHolesProps)
        {
            var refFinder = new ReferenceIntersectionFinder(doc, mepElement, view3D);

            // Получаем результаты (с увеличенным maxGap в самом refFinder, чтобы пробивать клапаны)
            List<IntersecResult> intersectResults = refFinder.GetIntersections(hostCategory);

            // Запас для вылета семейства (по 50 мм с каждой стороны = 100 мм)
            double extraLength = UnitUtils.ConvertToInternalUnits(100, UnitTypeId.Millimeters);

            foreach (var result in intersectResults)
            {
                // Берем чистую ссылку (Reference) из нашего результата
                Reference sourceRef = result.SourceReference;
                if (sourceRef == null) continue;

                outPoints.Add(result.Point);

                // Передаем ссылку (sourceRef), как и ожидает HolePropertiesFiller
                var holeFiller = new HolePropertiesFiller(doc, mepElement, sourceRef, result.Point);
                holeFiller.GetHoles(result.Point, _uiDocument, refFinder.Normal);

                // ПРИНУДИТЕЛЬНО ПЕРЕЗАПИСЫВАЕМ ТОЛЩИНУ
                // (Для этого убедись, что в HoleFamilyModel у SourceThickness есть { get; set; })
                foreach (var holeModel in holeFiller.HolesProps)
                {
                    holeModel.SourceThickness = result.Thickness + extraLength;
                }

                outHolesProps.AddRange(holeFiller.HolesProps);
            }
        }

        //public AllHolesDto FindHoles()
        //{
        //    var doc = _uiDocument.Document;
        //    var offset = 1.1;
        //    double pipeDiametrForFilter = 50;
        //    var settingsReader = new SettingsReader();
        //    var names = settingsReader.GetFamilyNames();


        //    //var recFamilyNames = settingsReader.GetFamilyTypes().FamilyTypes.Rectangled.FamilyNames;
        //    //var circledFamilyNames = settingsReader.GetFamilyTypes().FamilyTypes.Circled.FamilyNames;
        //    //var linkedDocs = GetLinkedDocuments(doc);

        //    var familyNames = new HashSet<string?>(names);
        //    var familyInstances = new FilteredElementCollector(doc)
        //        .WhereElementIsNotElementType()
        //        .OfClass(typeof(FamilyInstance))
        //        .Cast<FamilyInstance>()
        //        .Where(fi => familyNames.Contains(
        //            fi.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?
        //                .AsValueString()))
        //        .ToArray();

        //    using TransactionGroup transactionGroup = new TransactionGroup(doc);

        //    //var collector3dView = new FilteredElementCollector(doc);
        //    //Func<View3D, bool> isNotTemplate = v3 => !(v3.IsTemplate);
        //    //View3D view3D = collector3dView
        //    //    .OfClass(typeof(View3D))
        //    //    .Cast<View3D>()
        //    //    .FirstOrDefault(isNotTemplate);

        //    //if (view3D == null) TaskDialog.Show("3d view is absent", "Create 3d view");

        //    View3D view3D = new FilteredElementCollector(doc)
        //    .OfClass(typeof(View3D))
        //    .Cast<View3D>()
        //    .FirstOrDefault(v => v.Name == "Hole_Check" && !v.IsTemplate);

        //    if (view3D == null)
        //    {
        //        TaskDialog.Show("Ошибка", "Создайте 3D вид с именем 'Hole_Check', отключите на нем границу подрезки (Section Box) и включите все связанные файлы!");
        //        return null; // Прерываем работу, так как без вида луч не полетит
        //    }


        //    List<PointData> pointDatas = familyInstances.Select(familyInstance => new PointData(familyInstance.Id, (familyInstance.Location as LocationPoint).Point)).ToList();

        //    var smallestDiametr = UnitUtils.ConvertToInternalUnits(pipeDiametrForFilter, UnitTypeId.Millimeters);

        //    var pipeCollector = new FilteredElementCollector(doc).OfClass(typeof(Pipe)).Cast<Pipe>()
        //        .Where(w => w.Diameter > smallestDiametr);

        //    List<HoleFamilyModel> wallHoles = new List<HoleFamilyModel>();

        //    //исходные точки
        //    var newIntersections = new List<XYZ>();

        //    foreach (Element pipeElement in pipeCollector)
        //    {
        //        ReferenceIntersectionFinder refFinder = new ReferenceIntersectionFinder(doc, pipeElement, view3D);
        //        refFinder.TestThickestLayerLogic(BuiltInCategory.OST_Walls);

        //        refFinder.GetStructuralReferences(BuiltInCategory.OST_Walls);

        //        if (refFinder.WallReferences.Count > 0)
        //        {
        //            // Получаем список наших объектов IntersecResult
        //            var intersectResults = refFinder.GetIntersectionsPoints(refFinder.WallReferences);

        //            // ИСПРАВЛЕНИЕ 1: Извлекаем только координаты (XYZ) для списка newIntersections
        //            newIntersections.AddRange(intersectResults.Select(r => r.Point));

        //            var holes = new List<HoleFamilyModel>();

        //            // ИСПРАВЛЕНИЕ 2: Перебираем наши готовые результаты, а не просто точки!
        //            foreach (var result in intersectResults)
        //            {
        //                // Берем элемент-основу прямо из нашего результата (это будет та самая несущая стена 400 мм)
        //                var sourceElement = result.SourceReference;
        //                if (sourceElement == null) continue;

        //                // Берем правильную центральную точку из результата
        //                var exactPoint = result.Point;

        //                // Передаем правильные данные в твой класс заполнения свойств
        //                var holeFiller = new HolePropertiesFiller(doc, pipeElement, sourceElement, exactPoint);
        //                holeFiller.GetHoles(exactPoint, _uiDocument, refFinder.Normal);

        //                holes = holeFiller.HolesProps;
        //                wallHoles.AddRange(holes);
        //            }
        //        }

        //        //    refFinder.GetStructuralReferences(BuiltInCategory.OST_Floors);
        //        //    if (refFinder.FloorReferences.Count > 0)
        //        //    {
        //        //        var points = refFinder.GetIntersectionsPoints(refFinder.FloorReferences);
        //        //        newIntersections.AddRange(points);

        //        //        int i = 0;

        //        //        var holes = new List<HoleFamilyModel>();
        //        //        foreach (var point in points)
        //        //        {
        //        //            var sourceElement = refFinder.FloorReferences[i];
        //        //            if (sourceElement == null) continue;
        //        //            var holeFiller = new HolePropertiesFiller(doc, pipeElement, sourceElement, point);
        //        //            holeFiller.GetHoles(point, _uiDocument, refFinder.Normal);
        //        //            holes = holeFiller.HolesProps;
        //        //            wallHoles.AddRange(holes);
        //        //        }

        //        //    }
        //        //}

        //        //TODO Last
        //        var allductCollector = new FilteredElementCollector(doc).OfClass(typeof(Duct)).Cast<Duct>();
        //        var ductCollector = new List<Duct>();

        //        foreach (var duct in allductCollector)
        //        {
        //            if (duct.DuctType.Shape == ConnectorProfileType.Round)
        //            {
        //                if (duct.Diameter > smallestDiametr)
        //                    ductCollector.Add(duct);
        //            }
        //            else
        //            {
        //                if (duct.Width > smallestDiametr)
        //                    ductCollector.Add(duct);
        //            }
        //        }

        //        //.Where(w => w.Width > smallestDiametr);

        //        //из коллекции труб создаем отверстия

        //        //foreach (Element pipeElement in ductCollector)
        //        //{
        //        //    ReferenceIntersectionFinder refFinder = new ReferenceIntersectionFinder(doc, pipeElement, view3D);

        //        //    refFinder.GetStructuralReferences(BuiltInCategory.OST_Walls);

        //        //    if (refFinder.WallReferences.Count > 0)
        //        //    {
        //        //        var points = refFinder.GetIntersectionsPoints(refFinder.WallReferences);

        //        //        newIntersections.AddRange(points);
        //        //        int i = 0;

        //        //        var holes = new List<HoleFamilyModel>();
        //        //        foreach (var point in points)
        //        //        {
        //        //            var sourceElement = refFinder.WallReferences[i];
        //        //            if (sourceElement == null) continue;
        //        //            var holeFiller = new HolePropertiesFiller(doc, pipeElement, sourceElement, point);
        //        //            holeFiller.GetHoles(point, _uiDocument, refFinder.Normal);
        //        //            holes = holeFiller.HolesProps;
        //        //            wallHoles.AddRange(holes);
        //        //        }

        //        //    }

        //        //    refFinder.GetStructuralReferences(BuiltInCategory.OST_Floors);
        //        //    if (refFinder.FloorReferences.Count > 0)
        //        //    {
        //        //        var points = refFinder.GetIntersectionsPoints(refFinder.FloorReferences);
        //        //        newIntersections.AddRange(points);

        //        //        var i = 0;

        //        //        var holes = new List<HoleFamilyModel>();
        //        //        foreach (var point in points)
        //        //        {
        //        //            var sourceElement = refFinder.FloorReferences[i];
        //        //            if (sourceElement == null) continue;
        //        //            var holeFiller = new HolePropertiesFiller(doc, pipeElement, sourceElement, point);
        //        //            holeFiller.GetHoles(point, _uiDocument, refFinder.Normal);
        //        //            holes = holeFiller.HolesProps;
        //        //            wallHoles.AddRange(holes);
        //        //        }

        //        //    }
        //    }

        //    List<ActualHoleModelDto> actualHoles = new();
        //    List<NewHolesDto> newHoles = new();
        //    List<OutdatedFamilyDto> outdatedFamilies = new();

        //    // если есть семейства значит не первый раз вставляются семейства отверстий

        //    if (pointDatas.Count > 0)
        //    {
        //        var familyPoints = pointDatas.Select(x => x.Point);
        //        List<PointData> actualPoints =
        //            (from intersection in newIntersections
        //             from pointData in pointDatas
        //             where intersection.IsAlmostEqualTo(pointData.Point)
        //             select pointData)
        //            .ToList();

        //        //var actualPoints = pointDatas.Where(pd => newIntersections.Any(xyz =>xyz.Equals(pd.Point)));

        //        //var actualPoints = newIntersections.Intersect(familyPoints, new XYZComparer()).ToArray();

        //        var newPoints = newIntersections.Except(familyPoints, new XYZComparer()).ToArray(); // newIntersections.Except(familyPoints)
        //        var deletedPoints = familyPoints.Except(newIntersections, new XYZComparer()).ToArray(); // familyPoints.Except(newIntersections)

        //        foreach (var actualPoint in actualPoints)
        //        {
        //            var actualModels = wallHoles.Where(holeFamilyModel =>
        //                actualPoint.Point.IsAlmostEqualTo(holeFamilyModel.IntersectionPoint));

        //            actualHoles.AddRange(actualModels.Select(actualModel =>
        //                new ActualHoleModelDto(
        //                    actualPoint.Id.IntegerValue,
        //                    new PointDTO(actualModel.IntersectionPoint.X, actualModel.IntersectionPoint.Y, actualModel.IntersectionPoint.Z),
        //                    actualModel.IntersectingElementName,
        //                    actualModel.IntersectingElementType,
        //                    actualModel.SourceType,
        //                    UnitUtils.ConvertFromInternalUnits(actualModel.IntersectingElementTypeSize, UnitTypeId.Millimeters),
        //                    actualModel.SourceType,
        //                    true,
        //                    20,
        //                    false,
        //                    actualModel.SourceThickness,
        //                    new PointDTO(actualModel.IntersectionPoint.X, actualModel.IntersectionPoint.Y, actualModel.IntersectionPoint.Z),
        //                    actualModel.IntersectingElementWidth,
        //                    actualModel.SourceName
        //                )));
        //        }

        //        foreach (var newPoint in newPoints)
        //        {
        //            var newModels = wallHoles.Where(holeFamilyModel =>
        //                newPoint == holeFamilyModel.IntersectionPoint);

        //            newHoles.AddRange(
        //                newModels.Select(actualModel =>
        //                    new NewHolesDto(
        //                        actualModel.IntersectingElement.Id.IntegerValue,
        //                        new PointDTO(actualModel.IntersectionPoint.X, actualModel.IntersectionPoint.Y, actualModel.IntersectionPoint.Z),
        //                        actualModel.IntersectingElementType,
        //                        actualModel.IntersectingElementName,
        //                        actualModel.SourceType,
        //                        UnitUtils.ConvertFromInternalUnits(actualModel.IntersectingElementTypeSize, UnitTypeId.Millimeters),
        //                        actualModel.SourceType,
        //                        "Square",
        //                        20,
        //                        true,
        //                        actualModel.SourceThickness,
        //                        new PointDTO(actualModel.Normal.X, actualModel.Normal.Y, actualModel.Normal.Z),
        //                        actualModel.SourceThickness,
        //                        actualModel.SourceName,
        //                        actualModel.IntersectingElementHeight,
        //                        actualModel.IntersectingElementWidth)));
        //        }

        //        foreach (var deletedPoint in deletedPoints)
        //        {
        //            foreach (var family in familyInstances)
        //            {
        //                var loc = family.Location as LocationPoint;

        //                if (deletedPoint.IsAlmostEqualTo(loc?.Point))
        //                {

        //                    outdatedFamilies.Add(_entityToDTOConverter.Convert(family));
        //                }
        //            }
        //        }
        //    }
        //    else
        //    {
        //        //TODO have to delete family not intersecting object
        //        foreach (var hole in wallHoles)
        //        {
        //            var holeModel = new NewHolesDto(
        //                hole.IntersectingElement.Id.IntegerValue,
        //                new PointDTO(hole.IntersectionPoint.X, hole.IntersectionPoint.Y, hole.IntersectionPoint.Z),
        //                hole.IntersectingElementType,
        //                hole.IntersectingElementName,
        //                "",
        //                UnitUtils.ConvertFromInternalUnits(hole.IntersectingElementTypeSize, UnitTypeId.Millimeters),
        //                hole.SourceType,
        //                "Square",
        //                hole.HoleOffset,
        //                hole.IsInsert,
        //                hole.SourceThickness,
        //                new PointDTO(hole.Normal.X, hole.Normal.Y, hole.Normal.Z),
        //                hole.SourceThickness,
        //                hole.SourceName,
        //                hole.IntersectingElementHeight,
        //                hole.IntersectingElementWidth
        //            );

        //            newHoles.Add(holeModel);

        //        }
        //    }

        //    return new AllHolesDto(newHoles, actualHoles, outdatedFamilies);
        //}

        public AllHolesDto FindHoles()
        {
            var doc = _uiDocument.Document;
            double pipeDiametrForFilter = 50;
            var settingsReader = new SettingsReader();
            var names = settingsReader.GetFamilyNames();

            var familyNames = new HashSet<string?>(names);
            var familyInstances = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Where(fi => familyNames.Contains(fi.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString()))
                .ToArray();

            // Существующие точки семейств (для DTO)
            List<PointData> pointDatas = familyInstances
                .Select(fi => new PointData(fi.Id, (fi.Location as LocationPoint).Point))
                .ToList();

            // Списки для результатов
            List<XYZ> newIntersections = new List<XYZ>();
            List<HoleFamilyModel> allHolesProps = new List<HoleFamilyModel>();

            // 1. Создаем технический вид
            View3D raycastView = GetOrCreateRaycastView(doc);
            if (raycastView == null) return null;

            try
            {
                // 2. Получаем всю инженерку (трубы + воздуховоды)
                var mepElements = GetMEPElements(doc, pipeDiametrForFilter);

                // 3. Пускаем лучи
                foreach (Element mepElement in mepElements)
                {
                    // Пробиваем СТЕНЫ
                    ProcessIntersections(doc, mepElement, BuiltInCategory.OST_Walls, raycastView, newIntersections, allHolesProps);

                    // Пробиваем ПЕРЕКРЫТИЯ
                    ProcessIntersections(doc, mepElement, BuiltInCategory.OST_Floors, raycastView, newIntersections, allHolesProps);
                }
            }
            finally
            {
                allHolesProps = CleanupDuplicateHoles(allHolesProps, 400);

                // 2. Обновляем список координат newIntersections, чтобы он соответствовал очищенным отверстиям
                // Это важно для корректной работы DTO анализа!
                newIntersections = allHolesProps.Select(h => h.IntersectionPoint).ToList();

                // Удаляем технический вид в любом случае
                using (Transaction t = new Transaction(doc, "Delete View"))
                {
                    t.Start();
                    doc.Delete(raycastView.Id);
                    t.Commit();
                }
            }

            // --- ТВОЯ ЛОГИКА АНАЛИЗА DTO ---
            List<ActualHoleModelDto> actualHoles = new();
            List<NewHolesDto> newHoles = new();
            List<OutdatedFamilyDto> outdatedFamilies = new();

            if (pointDatas.Count > 0)
            {
                // ОПТИМИЗАЦИЯ: Используем Dictionary для быстрого поиска по точкам вместо LINQ O(n²)
                var pointDataDictionary = CreatePointDataDictionary(pointDatas);
                var familyPoints = pointDatas.Select(x => x.Point).ToList();

                // Находим существующие точки (оптимизировано)
                List<PointData> actualPoints = new();
                List<XYZ> newPointsList = new();

                foreach (var intersection in newIntersections)
                {
                    if (TryFindPointData(intersection, pointDataDictionary, out var pointData))
                    {
                        actualPoints.Add(pointData);
                    }
                    else
                    {
                        newPointsList.Add(intersection);
                    }
                }

                var newPoints = newPointsList.ToArray();
                var deletedPoints = FindDeletedPoints(familyPoints, newIntersections).ToArray();

                // Формируем ActualHoles (с оптимизацией через Dictionary)
                var holeCacheByPoint = CreateHoleCacheByPoint(allHolesProps);

                foreach (var actualPoint in actualPoints)
                {
                    if (TryGetHolesAtPoint(actualPoint.Point, holeCacheByPoint, out var actualModels))
                    {
                        actualHoles.AddRange(actualModels.Select(actualModel =>
                            new ActualHoleModelDto(
                                actualPoint.Id.IntegerValue,
                                new PointDTO(actualModel.IntersectionPoint.X, actualModel.IntersectionPoint.Y, actualModel.IntersectionPoint.Z),
                                actualModel.IntersectingElementName,
                                actualModel.IntersectingElementType,
                                actualModel.SourceType,
                                UnitUtils.ConvertFromInternalUnits(actualModel.IntersectingElementTypeSize, UnitTypeId.Millimeters),
                                actualModel.SourceType,
                                true,
                                20,
                                false,
                                actualModel.SourceThickness,
                                new PointDTO(actualModel.IntersectionPoint.X, actualModel.IntersectionPoint.Y, actualModel.IntersectionPoint.Z),
                                actualModel.IntersectingElementWidth,
                                actualModel.SourceName
                            )));
                    }
                }

                // Формируем NewHoles
                foreach (var newPoint in newPoints)
                {
                    if (TryGetHolesAtPoint(newPoint, holeCacheByPoint, out var newModels))
                    {
                        newHoles.AddRange(newModels.Select(actualModel =>
                            new NewHolesDto(
                                actualModel.IntersectingElement.Id.IntegerValue,
                                new PointDTO(actualModel.IntersectionPoint.X, actualModel.IntersectionPoint.Y, actualModel.IntersectionPoint.Z),
                                actualModel.IntersectingElementType,
                                actualModel.IntersectingElementName,
                                actualModel.SourceType,
                                UnitUtils.ConvertFromInternalUnits(actualModel.IntersectingElementTypeSize, UnitTypeId.Millimeters),
                                actualModel.SourceType,
                                "Square",
                                20,
                                true,
                                actualModel.SourceThickness,
                                new PointDTO(actualModel.Normal.X, actualModel.Normal.Y, actualModel.Normal.Z),
                                actualModel.SourceThickness,
                                actualModel.SourceName,
                                actualModel.IntersectingElementHeight,
                                actualModel.IntersectingElementWidth
                            )));
                    }
                }

                // Формируем OutdatedFamilies
                foreach (var deletedPoint in deletedPoints)
                {
                    foreach (var family in familyInstances)
                    {
                        var loc = family.Location as LocationPoint;
                        if (deletedPoint.IsAlmostEqualTo(loc?.Point))
                        {
                            outdatedFamilies.Add(_entityToDTOConverter.Convert(family));
                        }
                    }
                }
            }
            else
            {
                // Вставка с нуля
                foreach (var hole in allHolesProps)
                {
                    var holeModel = new NewHolesDto(
                        hole.IntersectingElement.Id.IntegerValue,
                        new PointDTO(hole.IntersectionPoint.X, hole.IntersectionPoint.Y, hole.IntersectionPoint.Z),
                        hole.IntersectingElementType,
                        hole.IntersectingElementName,
                        "",
                        UnitUtils.ConvertFromInternalUnits(hole.IntersectingElementTypeSize, UnitTypeId.Millimeters),
                        hole.SourceType,
                        "Square",
                        hole.HoleOffset,
                        hole.IsInsert,
                        hole.SourceThickness,
                        new PointDTO(hole.Normal.X, hole.Normal.Y, hole.Normal.Z),
                        hole.SourceThickness,
                        hole.SourceName,
                        hole.IntersectingElementHeight,
                        hole.IntersectingElementWidth
                    );
                    newHoles.Add(holeModel);
                }
            }

            return new AllHolesDto(newHoles, actualHoles, outdatedFamilies);
        }

        private View3D GetOrCreateRaycastView(Document doc)
        {
            string viewName = "Hole_Check_API"; // Уникальное имя, чтобы не мешать пользователю

            // 1. Пытаемся найти уже существующий вид
            View3D view3D = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v => v.Name == viewName && !v.IsTemplate);

            // Если нашли - отлично, отдаем его
            if (view3D != null) return view3D;

            // 2. Если вида нет, создаем новый
            using (Transaction t = new Transaction(doc, "Создание технического 3D вида"))
            {
                t.Start();

                // Ищем системный тип для 3D вида
                ViewFamilyType viewFamilyType = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(x => x.ViewFamily == ViewFamily.ThreeDimensional);

                if (viewFamilyType == null)
                {
                    t.RollBack();
                    throw new Exception("В проекте нет базового типа 3D вида!");
                }

                // Создаем вид
                view3D = View3D.CreateIsometric(doc, viewFamilyType.Id);
                view3D.Name = viewName;

                // ВАЖНО: Отключаем границу подрезки (Section Box), чтобы луч летел через всё здание
                if (view3D.IsSectionBoxActive)
                {
                    view3D.IsSectionBoxActive = false;
                }

                t.Commit();
            }

            return view3D;
        }

        /// <summary>
        /// ОПТИМИЗАЦИЯ: Создает кэш отверстий по их точкам пересечения для быстрого O(1) поиска
        /// </summary>
        private Dictionary<string, List<HoleFamilyModel>> CreateHoleCacheByPoint(List<HoleFamilyModel> holes)
        {
            var cache = new Dictionary<string, List<HoleFamilyModel>>();

            foreach (var hole in holes)
            {
                var key = GetPointKey(hole.IntersectionPoint);

                if (!cache.ContainsKey(key))
                {
                    cache[key] = new List<HoleFamilyModel>();
                }

                cache[key].Add(hole);
            }

            return cache;
        }

        /// <summary>
        /// ОПТИМИЗАЦИЯ: Пытается получить отверстия в точке с использованием кэша
        /// </summary>
        private bool TryGetHolesAtPoint(XYZ point, Dictionary<string, List<HoleFamilyModel>> holeCacheByPoint, out List<HoleFamilyModel> result)
        {
            var key = GetPointKey(point);

            if (holeCacheByPoint.TryGetValue(key, out var holes))
            {
                // Дополнительная фильтрация для надежности
                result = holes.Where(h => point.IsAlmostEqualTo(h.IntersectionPoint)).ToList();
                return result.Count > 0;
            }

            result = new List<HoleFamilyModel>();
            return false;
        }

        /// <summary>
        /// ОПТИМИЗАЦИЯ: Создает словарь точек с привязкой к их данным для быстрого O(1) поиска
        /// Группирует точки по округленным координатам для поиска с допуском
        /// </summary>
        private Dictionary<string, PointData> CreatePointDataDictionary(List<PointData> pointDatas)
        {
            var dictionary = new Dictionary<string, PointData>();

            foreach (var pointData in pointDatas)
            {
                // Округляем координаты до миллиметра для группировки
                var key = GetPointKey(pointData.Point);
                if (!dictionary.ContainsKey(key))
                {
                    dictionary[key] = pointData;
                }
            }

            return dictionary;
        }

        /// <summary>
        /// ОПТИМИЗАЦИЯ: Создает ключ для точки с округлением до миллиметра
        /// Это позволяет искать точки с геометрическим допуском
        /// </summary>
        private string GetPointKey(XYZ point)
        {
            // Толщина в мм, используется как масштаб округления
            const double tolerance = 0.00328084; // ~1 мм в футах (внутренние единицы Revit)

            var roundedX = Math.Round(point.X / tolerance) * tolerance;
            var roundedY = Math.Round(point.Y / tolerance) * tolerance;
            var roundedZ = Math.Round(point.Z / tolerance) * tolerance;

            return $"{roundedX:F6}|{roundedY:F6}|{roundedZ:F6}";
        }

        /// <summary>
        /// ОПТИМИЗАЦИЯ: Пытается найти PointData по координатам с геометрическим допуском
        /// </summary>
        private bool TryFindPointData(XYZ intersection, Dictionary<string, PointData> pointDataDictionary, out PointData result)
        {
            var key = GetPointKey(intersection);

            if (pointDataDictionary.TryGetValue(key, out var pointData))
            {
                // Финальная проверка с IsAlmostEqualTo для надежности
                if (intersection.IsAlmostEqualTo(pointData.Point))
                {
                    result = pointData;
                    return true;
                }
            }

            result = null;
            return false;
        }

        /// <summary>
        /// ОПТИМИЗАЦИЯ: Находит удаленные точки более эффективно чем Except()
        /// </summary>
        private IEnumerable<XYZ> FindDeletedPoints(List<XYZ> familyPoints, List<XYZ> newIntersections)
        {
            var newPointsSet = new HashSet<string>(newIntersections.Select(p => GetPointKey(p)));

            foreach (var familyPoint in familyPoints)
            {
                var key = GetPointKey(familyPoint);
                if (!newPointsSet.Contains(key))
                {
                    yield return familyPoint;
                }
            }
        }

        public void GetWalls(Document document, Reference pipeRef, Element pipeElem)
        {

            LocationCurve lc = pipeElem.Location as LocationCurve;
            Curve curve = lc.Curve;

            ReferenceComparer reference1 = new ReferenceComparer();

            ElementFilter filter = new
                ElementCategoryFilter(BuiltInCategory.OST_Walls);

            FilteredElementCollector collector = new FilteredElementCollector(document);
            Func<View3D, bool> isNotTemplate = v3 => !(v3.IsTemplate);
            View3D view3D = collector.OfClass(typeof(View3D)).Cast<View3D>()
                .First<View3D>(isNotTemplate);

            ReferenceIntersector refIntersector = new ReferenceIntersector(filter, FindReferenceTarget.Element, view3D);
            refIntersector.FindReferencesInRevitLinks = true;
            IList<ReferenceWithContext> referenceWithContext = refIntersector.Find(curve.GetEndPoint(0), (curve as Line).Direction);
            IList<Reference> references = referenceWithContext.Select(p => p.GetReference()).Distinct(reference1).Where(p => p.GlobalPoint.DistanceTo(curve.GetEndPoint(0)) < curve.Length).ToList();
            IList<Element> walls = new List<Element>();
            foreach (Reference reference in references)
            {
                RevitLinkInstance instance = document.GetElement(reference) as RevitLinkInstance;
                Document linkDoc = instance.GetLinkDocument();
                Element element = linkDoc.GetElement(reference.LinkedElementId);
                walls.Add(element);
            }
            TaskDialog.Show("Count of wall", walls.Count.ToString());
        }
    }
}
