using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using Juru.RevitPlugins.Openings.Contracts;
using Juru.RevitPlugins.Openings.Settings;

namespace Juru.RevitPlugins.Openings.Services
{
    public class ArchitectHoleService : IArchitectHoleService
    {
        private readonly UIDocument _uiDoc;
        private readonly Document _doc;
        private readonly ISettingsReader _settingsReader;

        // DI-контейнер сам передаст сюда нужные объекты!
        public ArchitectHoleService(UIDocument uiDoc, ISettingsReader settingsReader)
        {
            _uiDoc = uiDoc;
            _doc = uiDoc.Document;
            _settingsReader = settingsReader;
        }
        public void PlaceHolesFromLink()
        {
            // 1. Ищем связанный файл
            RevitLinkInstance linkInstance = new FilteredElementCollector(_doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .FirstOrDefault(l => l.GetLinkDocument() != null);

            if (linkInstance == null) return;

            Document linkDoc = linkInstance.GetLinkDocument();
            Transform linkTransform = linkInstance.GetTotalTransform();

            // 2. Читаем настройки
            var mepFamilyNames = _settingsReader.GetFamilyNames();
            var arMappings = _settingsReader.GetArMappings();

            // 3. Собираем задания инженеров
            var mepHoles = new FilteredElementCollector(linkDoc)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Where(fi => mepFamilyNames.Contains(fi.Symbol.FamilyName))
                .ToList();

            // --- ОБЪЯВЛЯЕМ ВСЕ СЧЕТЧИКИ ЗДЕСЬ ---
            int totalFound = mepHoles.Count;
            int successfullyPlaced = 0;
            int errorCount = 0;
            int skippedNoMapping = 0;
            int skippedNoHost = 0;

            TaskDialog.Show("Старт", $"Найдено инженерных заданий: {totalFound}");

            using (Transaction t = new Transaction(_doc, "Расстановка отверстий АР"))
            {
                t.Start();

                foreach (var mepHole in mepHoles)
                {
                    string mepName = mepHole.Symbol.FamilyName;
                    if (!arMappings.TryGetValue(mepName, out MappingInfo mapping) || mapping == null)
                    {
                        skippedNoMapping++;
                        continue;
                    }

                    LocationPoint lp = mepHole.Location as LocationPoint;
                    if (lp == null) continue;

                    // Берем чистую точку прямо из связи (без вычитания zShift, доверяем уровню)
                    XYZ arPoint = linkTransform.OfPoint(lp.Point);

                    bool isWallHole = mepName.ToLower().Contains("wall") || mepName.ToLower().Contains("стена");
                    Element host = FindHostInAR(arPoint, isWallHole);

                    if (host == null)
                    {
                        skippedNoHost++;
                        continue;
                    }

                    FamilySymbol arSymbol = GetFamilySymbol(mapping.ARFamilyName);
                    if (arSymbol == null) continue;
                    if (!arSymbol.IsActive) arSymbol.Activate();

                    // --- БЕЗОПАСНАЯ ВСТАВКА С ТРЮКАМИ BIM4P ---
                    using (SubTransaction subT = new SubTransaction(_doc))
                    {
                        try
                        {
                            subT.Start();

                            // СЕКРЕТ №1: Получаем уровень стены
                            Level hostLevel = _doc.GetElement(host.LevelId) as Level;

                            // СЕКРЕТ №2: Вставляем, явно передавая уровень
                            FamilyInstance arHole = _doc.Create.NewFamilyInstance(arPoint, arSymbol, host, hostLevel, StructuralType.NonStructural);

                            // СЕКРЕТ №3: Регенерируем документ ДО передачи параметров (чиним баг Revit)
                            _doc.Regenerate();

                            // Передаем размеры
                            CopyParameters(mepHole, arHole, mapping);

                            // Финальная регенерация
                            _doc.Regenerate();

                            subT.Commit();
                            successfullyPlaced++; // Увеличиваем счетчик успеха
                        }
                        catch (Exception)
                        {
                            subT.RollBack();
                            errorCount++; // Увеличиваем счетчик ошибок геометрии
                        }
                    }
                }

                t.Commit();
            }

            // --- ФОРМИРУЕМ ОТЧЕТ ---
            string report = $"Отчет по расстановке:\n\n" +
                            $"- Всего заданий найдено: {totalFound}\n" +
                            $"- Успешно вставлено: {successfullyPlaced}\n\n" +
                            $"- Пропущено (нет маппинга): {skippedNoMapping}\n" +
                            $"- Пропущено (нет основы/стены): {skippedNoHost}\n" +
                            $"- Ошибка Revit (не прорезало стену): {errorCount}";

            TaskDialog.Show("Результат", report);
        }
        //public void PlaceHolesFromLink()
        //{
        //    RevitLinkInstance linkInstance = new FilteredElementCollector(_doc)
        //        .OfClass(typeof(RevitLinkInstance))
        //        .Cast<RevitLinkInstance>()
        //        .FirstOrDefault(l => l.GetLinkDocument() != null);

        //    if (linkInstance == null) return;

        //    Document linkDoc = linkInstance.GetLinkDocument();
        //    Transform linkTransform = linkInstance.GetTotalTransform();

        //    var mepFamilyNames = _settingsReader.GetFamilyNames();
        //    var arMappings = _settingsReader.GetArMappings();

        //    var mepHoles = new FilteredElementCollector(linkDoc)
        //        .OfClass(typeof(FamilyInstance))
        //        .Cast<FamilyInstance>()
        //        .Where(fi => mepFamilyNames.Contains(fi.Symbol.FamilyName))
        //        .ToList();

        //    int totalFound = mepHoles.Count;
        //    int skippedNoMapping = 0;
        //    int skippedNoHost = 0;

        //    // --- ПОДГОТОВКА К ПОДСЧЕТУ ---
        //    // Получаем список уникальных имен АР-семейств из наших настроек
        //    var arFamilyNamesToCount = arMappings.Values.Select(m => m.ARFamilyName).Distinct().ToList();

        //    // Локальная функция для быстрого подсчета АР-отверстий в проекте
        //    int GetArHolesCount()
        //    {
        //        return new FilteredElementCollector(_doc)
        //            .OfClass(typeof(FamilyInstance))
        //            .Cast<FamilyInstance>()
        //            .Count(fi => fi.Symbol != null && arFamilyNamesToCount.Contains(fi.Symbol.FamilyName));
        //    }

        //    // Считаем, сколько отверстий было ДО запуска
        //    int countBefore = GetArHolesCount();

        //    TaskDialog.Show("Старт", $"Найдено инженерных заданий: {totalFound}");

        //    using (Transaction t = new Transaction(_doc, "Расстановка отверстий АР"))
        //    {
        //        t.Start();

        //        foreach (var mepHole in mepHoles)
        //        {
        //            string mepName = mepHole.Symbol.FamilyName;
        //            if (!arMappings.TryGetValue(mepName, out MappingInfo mapping) || mapping == null)
        //            {
        //                TaskDialog.Show("Debug", $"Нет в маппинге: {mepName}");
        //                skippedNoMapping++;
        //                continue;
        //            }

        //            LocationPoint lp = mepHole.Location as LocationPoint;
        //            if (lp == null) continue;

        //            XYZ arPoint = linkTransform.OfPoint(lp.Point);

        //            double zShift = 0;
        //            if (mapping.IsRect)
        //            {
        //                Parameter mepHeight = mepHole.LookupParameter("Height");
        //                if (mepHeight != null) zShift = mepHeight.AsDouble() / 2.0;
        //            }
        //            else
        //            {
        //                Parameter mepDiameter = mepHole.LookupParameter("Diameter");
        //                if (mepDiameter != null) zShift = mepDiameter.AsDouble() / 2.0;
        //            }

        //            XYZ finalInsertPoint = new XYZ(arPoint.X, arPoint.Y, arPoint.Z - zShift);

        //            bool isWallHole = mepName.ToLower().Contains("wall") || mepName.ToLower().Contains("стена");
        //            Element host = FindHostInAR(finalInsertPoint, isWallHole);

        //            //    if (host == null)
        //            //    {
        //            //            TaskDialog.Show("Debug", $"Нет основы для отверстия {mepName}на точке: {finalInsertPoint}");
        //            //            DrawDebugCross(_doc, finalInsertPoint);
        //            //        skippedNoHost++;
        //            //        continue;
        //            //    }

        //            //    FamilySymbol arSymbol = GetFamilySymbol(mapping.ARFamilyName);
        //            //    if (arSymbol == null)
        //            //    {
        //            //        TaskDialog.Show("Ошибка", $"Семейство {mapping.ARFamilyName} не загружено в проект!");
        //            //        continue;
        //            //    }

        //            //    if (!arSymbol.IsActive) arSymbol.Activate();

        //            //    FamilyInstance arHole = _doc.Create.NewFamilyInstance(finalInsertPoint, arSymbol, host, StructuralType.NonStructural);
        //            //    CopyParameters(mepHole, arHole, mapping);
        //            //}

        //            if (host == null)
        //            {
        //                skippedNoHost++;
        //                continue;
        //            }

        //            FamilySymbol arSymbol = GetFamilySymbol(mapping.ARFamilyName);
        //            if (arSymbol == null) continue;
        //            if (!arSymbol.IsActive) arSymbol.Activate();

        //            using (SubTransaction subT = new SubTransaction(_doc))
        //            {
        //                try
        //                {
        //                    subT.Start();

        //                    // СЕКРЕТ №1: Получаем уровень стены
        //                    Level hostLevel = _doc.GetElement(host.LevelId) as Level;

        //                    // Вставляем, используя 5-аргументную перегрузку
        //                    FamilyInstance arHole = _doc.Create.NewFamilyInstance(arPoint, arSymbol, host, hostLevel, StructuralType.NonStructural);

        //                    // СЕКРЕТ №2: Регенерируем документ ДО передачи параметров
        //                    _doc.Regenerate();

        //                    // СЕКРЕТ №3: Передаем параметры (ширину и высоту)
        //                    CopyParameters(mepHole, arHole, mapping);

        //                    // Еще одна регенерация после изменения размеров, чтобы Revit точно прорезал стену
        //                    _doc.Regenerate();

        //                    subT.Commit();
        //                    successfullyPlaced++;
        //                }
        //                catch (Exception)
        //                {
        //                    subT.RollBack();
        //                    errorCount++;
        //                }
        //            }


        //            // Применяем транзакцию. Здесь Revit сам удалит "косячные" отверстия
        //            t.Commit();
        //    }

        //    // --- ЧЕСТНЫЙ ПОДСЧЕТ ПОСЛЕ ТРАНЗАКЦИИ ---
        //    // Считаем, сколько отверстий стало ПОСЛЕ запуска
        //    int countAfter = GetArHolesCount();

        //    // Чистая математика
        //    int successfullyPlaced = countAfter - countBefore;

        //    // Считаем, сколько Revit удалил из-за ошибок геометрии (например, "not cutting anything")
        //    // (Всего попытались вставить) - (Успешно вставили)
        //    int attemptedToPlace = totalFound - skippedNoMapping - skippedNoHost;
        //    int errorCount = attemptedToPlace - successfullyPlaced;

        //    string report = $"Отчет по расстановке:\n\n" +
        //                    $"- Всего заданий найдено: {totalFound}\n" +
        //                    $"- Успешно добавлено в модель: {successfullyPlaced}\n\n" +
        //                    $"- Пропущено (нет маппинга): {skippedNoMapping}\n" +
        //                    $"- Пропущено (нет основы/стены): {skippedNoHost}\n" +
        //                    $"- Ошибка Revit (удалено/не прорезало): {errorCount}";

        //    TaskDialog.Show("Результат", report);

        //}

        private void CopyParameters(FamilyInstance mepHole, FamilyInstance arHole, MappingInfo mapping)
        {
            if (mapping.IsRect)
            {
                // Для прямоугольного отверстия
                // ВНИМАНИЕ: Проверь, чтобы параметры в МЕР-семействе точно назывались "Width" и "Height"
                Parameter mepWidth = mepHole.LookupParameter("Width");
                Parameter mepHeight = mepHole.LookupParameter("Height");

                if (mepWidth != null)
                    arHole.LookupParameter("ADSK_Отверстие_Ширина")?.Set(mepWidth.AsDouble());

                if (mepHeight != null)
                    arHole.LookupParameter("ADSK_Отверстие_Высота")?.Set(mepHeight.AsDouble());
            }
            else
            {
                // Для круглого отверстия
                Parameter mepDiameter = mepHole.LookupParameter("Diameter");

                if (mepDiameter != null)
                    arHole.LookupParameter("ADSK_Отверстие_Диаметр")?.Set(mepDiameter.AsDouble());
            }
        }



        private MappingInfo GetARMapping(string mepName, HoleFamilyTypes settings)
        {
            var circled = settings.FamilyTypes.Circled;
            var rect = settings.FamilyTypes.Rectangled;

            if (mepName == circled.FamilyNames.WallType)
                return new MappingInfo { ARFamilyName = circled.ArFamilyNames.WallType, IsRect = false };
            if (mepName == circled.FamilyNames.FloorType)
                return new MappingInfo { ARFamilyName = circled.ArFamilyNames.FloorType, IsRect = false };

            if (mepName == rect.FamilyNames.WallType)
                return new MappingInfo { ARFamilyName = rect.ArFamilyNames.WallType, IsRect = true };
            if (mepName == rect.FamilyNames.FloorType)
                return new MappingInfo { ARFamilyName = rect.ArFamilyNames.FloorType, IsRect = true };

            return null;
        }

        private Element FindHostInAR(XYZ point, bool isWall)
        {
            // Выбираем категорию для поиска
            BuiltInCategory category = isWall ? BuiltInCategory.OST_Walls : BuiltInCategory.OST_Floors;
            ElementCategoryFilter filter = new ElementCategoryFilter(category);

            // Создаем "кубик" размером 10х10х10 см вокруг нашей точки
            // Это нужно, потому что точка пересечения может быть чуть-чуть внутри стены
            double tolerance = UnitUtils.ConvertToInternalUnits(100, UnitTypeId.Millimeters);
            XYZ min = new XYZ(point.X - tolerance, point.Y - tolerance, point.Z - tolerance);
            XYZ max = new XYZ(point.X + tolerance, point.Y + tolerance, point.Z + tolerance);
            Outline searchOutline = new Outline(min, max);

            BoundingBoxIntersectsFilter bbFilter = new BoundingBoxIntersectsFilter(searchOutline);

            // Ищем стену/перекрытие, которое пересекается с нашим кубиком
            return new FilteredElementCollector(_doc)
                .WherePasses(filter)
                .WherePasses(bbFilter)
                .FirstOrDefault(e => e is Wall || e is Floor);
        }

        private FamilySymbol GetFamilySymbol(string familyName)
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(x => x.FamilyName == familyName);
        }

        private void DrawDebugCross(Document doc, XYZ point)
        {
            // Длина "лучей" крестика (например, 500 мм в каждую сторону)
            double length = UnitUtils.ConvertToInternalUnits(500, UnitTypeId.Millimeters);

            // 1. Плоскость XY (для линий X и Y)
            Plane planeXY = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, point);
            SketchPlane sketchPlaneXY = SketchPlane.Create(doc, planeXY);

            // Линия вдоль оси X
            Line lineX = Line.CreateBound(point - new XYZ(length, 0, 0), point + new XYZ(length, 0, 0));
            doc.Create.NewModelCurve(lineX, sketchPlaneXY);

            // Линия вдоль оси Y
            Line lineY = Line.CreateBound(point - new XYZ(0, length, 0), point + new XYZ(0, length, 0));
            doc.Create.NewModelCurve(lineY, sketchPlaneXY);

            // 2. Плоскость XZ (для вертикальной линии Z)
            Plane planeXZ = Plane.CreateByNormalAndOrigin(XYZ.BasisY, point);
            SketchPlane sketchPlaneXZ = SketchPlane.Create(doc, planeXZ);

            // Линия вдоль оси Z (вверх-вниз)
            Line lineZ = Line.CreateBound(point - new XYZ(0, 0, length), point + new XYZ(0, 0, length));
            doc.Create.NewModelCurve(lineZ, sketchPlaneXZ);
        }
    }
}
