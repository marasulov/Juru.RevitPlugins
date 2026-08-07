using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Linq;
using Juru.RevitPlugins.Openings.Models;

namespace Juru.RevitPlugins.Openings.Services
{
    public class ReferenceIntersectionFinder
    {
        private readonly Document _document;
        private readonly Curve _curve;
        private readonly View3D _view3D;
        private readonly Element _intersectingElement;

        public ReferenceIntersectionFinder(Document document, Element intersectingElement, View3D view3D)
        {
            _intersectingElement = intersectingElement;
            Reference reference = new Reference(intersectingElement);
            _document = document;
            _view3D = view3D;
            Element element = _document.GetElement(reference);
            LocationCurve lc = element.Location as LocationCurve;
            if (lc == null) return;
            StartPoint = lc.Curve.GetEndPoint(0);
            EndPoint = lc.Curve.GetEndPoint(1);
            Normal = EndPoint.Subtract(StartPoint).Normalize();

            _curve = lc.Curve;
        }

        //public void TestRawIntersections(BuiltInCategory category)
        //{
        //    ElementFilter filter = new ElementCategoryFilter(category);

        //    // Ищем именно грани (Face), чтобы видеть каждый слой сэндвича
        //    ReferenceIntersector refIntersector = new ReferenceIntersector(filter, FindReferenceTarget.Face, _view3D);
        //    refIntersector.FindReferencesInRevitLinks = true;

        //    IList<ReferenceWithContext> referenceWithContext = refIntersector.Find(StartPoint, Normal);

        //    // Берем с запасом длины + 1 фут (~300 мм), чтобы точно ничего не отрезать погрешностью
        //    var validRefs = referenceWithContext
        //        .Where(p => p.GetReference().GlobalPoint.DistanceTo(StartPoint) < _curve.Length + 1.0)
        //        .ToList();

        //    string message = $"Категория: {category}\n";
        //    message += $"Всего пробито граней (Face): {validRefs.Count}\n\n";

        //    foreach (var refWithContext in validRefs.OrderBy(r => r.Proximity))
        //    {
        //        var r = refWithContext.GetReference();
        //        double distanceMm = UnitUtils.ConvertFromInternalUnits(refWithContext.Proximity, UnitTypeId.Millimeters);

        //        // Определяем, чей это ID (из связи или из локального файла)
        //        ElementId id = r.LinkedElementId != ElementId.InvalidElementId ? r.LinkedElementId : r.ElementId;

        //        message += $"- Элемент ID: {id} | Расстояние от старта: {distanceMm:F1} мм\n";
        //    }

        //    // Выводим на экран
        //    TaskDialog.Show("Raw Raycast Debug", message);
        //}
        public void TestThickestLayerLogic(BuiltInCategory category)
        {
            ElementFilter filter = new ElementCategoryFilter(category);
            ReferenceIntersector refIntersector = new ReferenceIntersector(filter, FindReferenceTarget.Face, _view3D);
            refIntersector.FindReferencesInRevitLinks = true;

            // Пускаем луч
            var referenceWithContext = refIntersector.Find(StartPoint, Normal);

            FilteredElementCollector linkCollector = new FilteredElementCollector(_document, _view3D.Id);
            var visibleLinks = linkCollector.OfClass(typeof(RevitLinkInstance)).ToElements();

            if (visibleLinks.Count == 0)
            {
                TaskDialog.Show("Ошибка", "На текущем 3D виде не найдено ни одного видимого связанного файла!");
            }

            var validRefs = referenceWithContext
                .Where(p => p.GetReference().GlobalPoint.DistanceTo(StartPoint) < _curve.Length + 1.0)
                .Select(p => p.GetReference())
                .OrderBy(r => r.GlobalPoint.DistanceTo(StartPoint))
                .ToList();

            //if (validRefs.Count == 0)
            //{
            //    TaskDialog.Show("Test", "Луч никуда не попал.");
            //    return;
            //}

            string message = $"Категория: {category}\n\n";

            // 1. Собираем грани в "сэндвичи" (кластеры)
            double maxGap = UnitUtils.ConvertToInternalUnits(500, UnitTypeId.Millimeters);
            List<Reference> currentCluster = new List<Reference>();
            List<List<Reference>> allAssemblies = new List<List<Reference>>();

            foreach (var r in validRefs)
            {
                if (currentCluster.Count == 0)
                {
                    currentCluster.Add(r);
                    continue;
                }

                var lastPoint = currentCluster.Last().GlobalPoint;
                ElementId currentId = r.LinkedElementId != ElementId.InvalidElementId ? r.LinkedElementId : r.ElementId;
                ElementId lastId = currentCluster.Last().LinkedElementId != ElementId.InvalidElementId ? currentCluster.Last().LinkedElementId : currentCluster.Last().ElementId;

                // Если это тот же элемент или он стоит вплотную - это всё еще одна стена
                if (currentId == lastId || r.GlobalPoint.DistanceTo(lastPoint) <= maxGap)
                {
                    currentCluster.Add(r);
                }
                else
                {
                    allAssemblies.Add(new List<Reference>(currentCluster));
                    currentCluster.Clear();
                    currentCluster.Add(r);
                }
            }
            if (currentCluster.Count > 0) allAssemblies.Add(currentCluster);

            // 2. Анализируем каждый "сэндвич"
            int assemblyCount = 1;
            foreach (var assembly in allAssemblies)
            {
                message += $"=== Стена (Сборка) {assemblyCount} ===\n";

                // Группируем грани по ID элемента, чтобы получить отдельные слои
                var layers = assembly.GroupBy(r => r.LinkedElementId != ElementId.InvalidElementId ? r.LinkedElementId : r.ElementId).ToList();

                ElementId thickestId = ElementId.InvalidElementId;
                double maxThickness = 0;
                Dictionary<ElementId, double> layerThicknesses = new Dictionary<ElementId, double>();

                // Считаем толщину каждого слоя
                foreach (var layer in layers)
                {
                    var firstFace = layer.First().GlobalPoint;
                    var lastFace = layer.Last().GlobalPoint;

                    double thickness = UnitUtils.ConvertFromInternalUnits(firstFace.DistanceTo(lastFace), UnitTypeId.Millimeters);
                    layerThicknesses[layer.Key] = thickness;

                    if (thickness > maxThickness)
                    {
                        maxThickness = thickness;
                        thickestId = layer.Key;
                    }
                }

                // Выводим результат
                foreach (var layer in layerThicknesses)
                {
                    if (layer.Key == thickestId)
                        message += $"⭐ [ОСНОВА] ID: {layer.Key} | Толщина: {layer.Value:F1} мм\n";
                    else
                        message += $"- Слой ID: {layer.Key} | Толщина: {layer.Value:F1} мм\n";
                }
                message += "\n";
                assemblyCount++;
            }

            TaskDialog.Show("Логика несущего слоя", message);
        }

        public IList<Reference> WallReferences { get; } = new List<Reference>();
        public IList<Reference> FloorReferences { get; } = new List<Reference>();

        public double Thickness { get; private set; }

        public XYZ StartPoint;
        public XYZ EndPoint;
        public XYZ Normal { get; set; }


        //public List<HoleFamilyModel<Wall>> HolesProps { get; } = new List<HoleFamilyModel<Wall>>();
        //public List<HoleFamilyModel<Floor>> FloorHoles { get; } = new List<HoleFamilyModel<Floor>>();

        private List<Reference> GetAllReferences(BuiltInCategory builtInCategory)
        {
            ElementFilter filter = new ElementCategoryFilter(builtInCategory);

            ReferenceIntersector refIntersector
                = new ReferenceIntersector(filter, FindReferenceTarget.Element, _view3D);

            refIntersector.FindReferencesInRevitLinks = true;
            IList<ReferenceWithContext> referenceWithContext
                = refIntersector.Find(
                    StartPoint,
                    Normal);

            var references = referenceWithContext
                .Select(p => p.GetReference())
                .Where(p => p.GlobalPoint.DistanceTo(
                    _curve.GetEndPoint(0)) < _curve.Length)
                .ToList();

            return references;
        }

        //public List<XYZ> GetIntersectionsPoints(IList<Reference> references)
        //{
        //    var intersectPoints = new List<XYZ>();

        //    if (references.Count <= 0) return intersectPoints;


        //    for (int i = 0; i < references.Count; i+=2)
        //    {
        //        var firstFaceRef = references[i];
        //        var secondFaceRef = references[i + 1];

        //        if (firstFaceRef.ElementId != secondFaceRef.ElementId) return intersectPoints;
        //        Thickness = firstFaceRef.GlobalPoint.DistanceTo(secondFaceRef.GlobalPoint);
        //        var intPoint = new XYZ(
        //            (firstFaceRef.GlobalPoint.X + secondFaceRef.GlobalPoint.X) / 2,
        //            (firstFaceRef.GlobalPoint.Y + secondFaceRef.GlobalPoint.Y) / 2,
        //            firstFaceRef.GlobalPoint.Z);
        //        intersectPoints.Add(intPoint);
        //    }



        //    return intersectPoints;
        //}

        private class ElementLayerData
        {
            public ElementId Id { get; set; }
            public double StartDist { get; set; }
            public double EndDist { get; set; }
            public double Thickness { get; set; }
            public Reference FirstRef { get; set; }
            public Reference LastRef { get; set; }
        }

        // Главный публичный метод, который мы будем вызывать из сервиса!
        public List<IntersecResult> GetIntersections(BuiltInCategory category)
        {
            var intersectResults = new List<IntersecResult>();

            // 1. Пускаем луч (метод GetAllReferences должен искать Face, как мы обсуждали)
            var rawRefs = GetAllReferences(category);

            // 2. Выкидываем пустышки и оставляем только то, что принадлежит связанным файлам
            var validRefs = rawRefs.Where(r =>
                r != null &&
                _document.GetElement(r.ElementId) is RevitLinkInstance).ToList();

            if (validRefs.Count == 0) return intersectResults;

            // 3. ПРЕВРАЩАЕМ В ОТРЕЗКИ (Интервалы)
            var elementsIntervals = validRefs
                .GroupBy(r => r.LinkedElementId != ElementId.InvalidElementId ? r.LinkedElementId : r.ElementId)
                .Select(g =>
                {
                    var refs = g.OrderBy(r => r.GlobalPoint.DistanceTo(StartPoint)).ToList();
                    double startDist = refs.First().GlobalPoint.DistanceTo(StartPoint);
                    double endDist = refs.Last().GlobalPoint.DistanceTo(StartPoint);

                    return new ElementLayerData
                    {
                        Id = g.Key,
                        StartDist = startDist,
                        EndDist = endDist,
                        Thickness = endDist - startDist,
                        FirstRef = refs.First(),
                        LastRef = refs.Last()
                    };
                })
                .OrderBy(e => e.StartDist)
                .ToList();

            // Defensive check: ensure we have intervals
            if (elementsIntervals.Count == 0) return intersectResults;

            // 4. СКЛЕИВАЕМ ОТРЕЗКИ В ПИРОГИ СТЕН (Кластеризация)
            double maxGap = UnitUtils.ConvertToInternalUnits(50, UnitTypeId.Millimeters);
            var assemblies = new List<List<ElementLayerData>>();
            var currentAssembly = new List<ElementLayerData> { elementsIntervals[0] };
            double currentAssemblyEnd = elementsIntervals[0].EndDist;

            for (int i = 1; i < elementsIntervals.Count; i++)
            {
                var element = elementsIntervals[i];
                if (element.StartDist <= currentAssemblyEnd + maxGap)
                {
                    currentAssembly.Add(element);
                    if (element.EndDist > currentAssemblyEnd) currentAssemblyEnd = element.EndDist;
                }
                else
                {
                    assemblies.Add(currentAssembly);
                    currentAssembly = new List<ElementLayerData> { element };
                    currentAssemblyEnd = element.EndDist;
                }
            }
            assemblies.Add(currentAssembly);

            // 5. ВЫБИРАЕМ ОСНОВУ И ФОРМИРУЕМ РЕЗУЛЬТАТ
            foreach (var assembly in assemblies)
            {
                var thickestLayer = assembly.OrderByDescending(e => e.Thickness).First();
                var startFace = assembly.OrderBy(e => e.StartDist).First().FirstRef.GlobalPoint;
                var endFace = assembly.OrderBy(e => e.EndDist).Last().LastRef.GlobalPoint;

                var centerPoint = new XYZ(
                    (startFace.X + endFace.X) / 2,
                    (startFace.Y + endFace.Y) / 2,
                    (startFace.Z + endFace.Z) / 2
                );

                double totalThickness = startFace.DistanceTo(endFace);
                intersectResults.Add(new IntersecResult(centerPoint, totalThickness, thickestLayer.FirstRef));
            }

            var mergedResults = new List<IntersecResult>();

            foreach (var current in intersectResults)
            {
                if (mergedResults.Count == 0)
                {
                    mergedResults.Add(current);
                    continue;
                }

                var previous = mergedResults.Last();

                // Дистанция между центрами двух отверстий
                double distanceBetweenCenters = previous.Point.DistanceTo(current.Point);

                // Сумма половинок их толщин (т.е. если они касаются друг друга)
                double minAllowedDistance = (previous.Thickness / 2) + (current.Thickness / 2);

                if (distanceBetweenCenters <= minAllowedDistance)
                {
                    // Отверстия налезли друг на друга! Объединяем их в одно большое.

                    // Новая общая толщина - это расстояние между их крайними гранями
                    double newThickness = distanceBetweenCenters + (previous.Thickness / 2) + (current.Thickness / 2);

                    // Новый центр - ровно посередине между их старыми центрами
                    XYZ newCenter = new XYZ(
                        (previous.Point.X + current.Point.X) / 2,
                        (previous.Point.Y + current.Point.Y) / 2,
                        (previous.Point.Z + current.Point.Z) / 2
                    );

                    // Обновляем предыдущий результат (основу оставляем от самой толстой стены)
                    previous.Point = newCenter;
                    previous.Thickness = newThickness;
                }
                else
                {
                    mergedResults.Add(current);
                }
            }

            return mergedResults;

            //return intersectResults;
        }

        //public List<IntersecResult> GetIntersectionsPoints(IList<Reference> references)
        //{
        //    var intersectResults = new List<IntersecResult>();
        //    if (references.Count == 0) return intersectResults;

        //    // 1. Превращаем каждую стену в цельный "отрезок" (интервал)
        //    var elementsIntervals = references
        //        .GroupBy(r => r.LinkedElementId != ElementId.InvalidElementId ? r.LinkedElementId : r.ElementId)
        //        .Select(g =>
        //        {
        //            var refs = g.ToList();
        //            // Сортируем грани одной стены по удаленности от начала трубы
        //            var sortedRefs = refs.OrderBy(r => r.GlobalPoint.DistanceTo(StartPoint)).ToList();

        //            double startDist = sortedRefs.First().GlobalPoint.DistanceTo(StartPoint);
        //            double endDist = sortedRefs.Last().GlobalPoint.DistanceTo(StartPoint);

        //            return new ElementLayerData
        //            {
        //                Id = g.Key,
        //                StartDist = startDist,
        //                EndDist = endDist,
        //                Thickness = endDist - startDist,
        //                FirstRef = sortedRefs.First(),
        //                LastRef = sortedRefs.Last()
        //            };
        //        })
        //        .OrderBy(e => e.StartDist) // Сортируем отрезки по началу
        //        .ToList();

        //    // 2. Склеиваем отрезки, которые стоят вплотную, в "Сборки" (пироги)
        //    double maxGap = UnitUtils.ConvertToInternalUnits(50, UnitTypeId.Millimeters);

        //    var assemblies = new List<List<ElementLayerData>>();
        //    var currentAssembly = new List<ElementLayerData> { elementsIntervals[0] };
        //    double currentAssemblyEnd = elementsIntervals[0].EndDist;

        //    for (int i = 1; i < elementsIntervals.Count; i++)
        //    {
        //        var element = elementsIntervals[i];

        //        // Если стена начинается ДО того, как закончилась предыдущая + 50мм зазора
        //        if (element.StartDist <= currentAssemblyEnd + maxGap)
        //        {
        //            currentAssembly.Add(element);
        //            if (element.EndDist > currentAssemblyEnd)
        //            {
        //                currentAssemblyEnd = element.EndDist; // Расширяем границу пирога
        //            }
        //        }
        //        else
        //        {
        //            // Началась совсем другая стена (другая комната)
        //            assemblies.Add(currentAssembly);
        //            currentAssembly = new List<ElementLayerData> { element };
        //            currentAssemblyEnd = element.EndDist;
        //        }
        //    }
        //    assemblies.Add(currentAssembly);

        //    // 3. Анализируем каждую готовую сборку
        //    foreach (var assembly in assemblies)
        //    {
        //        // Находим самый толстый слой (нашу основу!)
        //        var thickestLayer = assembly.OrderByDescending(e => e.Thickness).First();

        //        // Находим крайние точки всего пирога стен (чтобы найти центр)
        //        var startFace = assembly.OrderBy(e => e.StartDist).First().FirstRef.GlobalPoint;
        //        var endFace = assembly.OrderBy(e => e.EndDist).Last().LastRef.GlobalPoint;

        //        var centerPoint = new XYZ(
        //            (startFace.X + endFace.X) / 2,
        //            (startFace.Y + endFace.Y) / 2,
        //            (startFace.Z + endFace.Z) / 2
        //        );

        //        double totalThickness = startFace.DistanceTo(endFace);

        //        // Создаем финальный результат: привязываем к несущему слою!
        //        intersectResults.Add(new IntersecResult(centerPoint, totalThickness, thickestLayer.FirstRef));
        //    }

        //    return intersectResults;

        //}

        //public void GetStructuralReferences(BuiltInCategory builtInCategory)
        //{
        //    var tempReferences = GetAllReferences(builtInCategory);
        //    if (tempReferences.Count <= 0) return;
        //    foreach (var r in tempReferences)
        //    {
        //        if (r == null) return;

        //        if (_document.GetElement(r.ElementId) is not RevitLinkInstance link) return;

        //        var ldoc = link.GetLinkDocument();

        //        Element el = ldoc.GetElement(r.LinkedElementId) as HostObject;
        //        double structuralParameter;

        //        //Dictionary<BuiltInCategory, BuiltInParameter> builtInParameters =
        //        //    new Dictionary<BuiltInCategory, BuiltInParameter>()
        //        //    {
        //        //        {BuiltInCategory.OST_Walls,  BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT},
        //        //        {BuiltInCategory.OST_Floors, BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL}

        //        //    };


        //        //structuralParameter = el.get_Parameter(builtInParameters[builtInCategory])
        //        //    .AsInteger();
        //        //if (structuralParameter != 1) continue;
        //        //WallReferences.Add(r);

        //        switch (builtInCategory)
        //        {
        //            case BuiltInCategory.OST_Walls:
        //                {
        //                    //structuralParameter = el.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT)
        //                    //    .AsInteger();
        //                    //if (structuralParameter != 1) continue;
        //                    WallReferences.Add(r);

        //                    break;
        //                }
        //            case BuiltInCategory.OST_Floors:
        //                {
        //                    structuralParameter =
        //                        el.get_Parameter(BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL).AsInteger();
        //                    if (structuralParameter != 1) continue;

        //                    FloorReferences.Add(r);
        //                    break;
        //                }
        //        }
        //    }
        //}
    }
}
