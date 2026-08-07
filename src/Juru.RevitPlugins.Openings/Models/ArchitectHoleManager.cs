using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Juru.RevitPlugins.Openings.Contracts;
using Juru.RevitPlugins.Openings.Settings;

namespace Juru.RevitPlugins.Openings.Models
{
    public class ArchitectHoleManager
    {
        private readonly Document _doc;
        private readonly SettingsReader _settingsReader;

        public ArchitectHoleManager(Document doc)
        {
            _doc = doc;
            _settingsReader = new SettingsReader();
        }

        public void PlaceHolesFromLink()
        {
            // 1. Находим связанный файл ОВ/ВК
            RevitLinkInstance linkInstance = new FilteredElementCollector(_doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .FirstOrDefault(l => l.GetLinkDocument() != null);

            if (linkInstance == null)
            {
                TaskDialog.Show("Ошибка", "Связанный файл не найден.");
                return;
            }

            Document linkDoc = linkInstance.GetLinkDocument();
            Transform linkTransform = linkInstance.GetTotalTransform();

            // 2. Получаем настройки и список имен семейств инженеров
            var settings = _settingsReader.GetFamilyTypes();
            var mepFamilyNames = _settingsReader.GetFamilyNames();

            // 3. Собираем все задания (семейства) из связи
            var mepHoles = new FilteredElementCollector(linkDoc)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Where(fi => mepFamilyNames.Contains(fi.Symbol.FamilyName))
                .ToList();

            using (Transaction t = new Transaction(_doc, "Расстановка отверстий по заданию"))
            {
                t.Start();

                foreach (var mepHole in mepHoles)
                {
                    string mepName = mepHole.Symbol.FamilyName;

                    // Определяем маппинг: какое АР семейство соответствует этому МЕР семейству
                    var mapping = GetARMapping(mepName, settings);
                    if (mapping == null) continue;

                    // Координаты: переводим точку из координат связи в координаты АР-файла
                    LocationPoint lp = mepHole.Location as LocationPoint;
                    if (lp == null) continue;
                    XYZ arPoint = linkTransform.OfPoint(lp.Point);

                    // 4. Ищем локальную основу (Стену или Перекрытие)
                    bool isWallHole = mepName.ToLower().Contains("wall");
                    Element host = FindHostInAR(arPoint, isWallHole);

                    if (host == null) continue;

                    // 5. Получаем символ (тип) АР отверстия
                    FamilySymbol arSymbol = GetFamilySymbol(mapping.ARFamilyName);
                    if (arSymbol == null) continue;
                    if (!arSymbol.IsActive) arSymbol.Activate();

                    // 6. ВСТАВКА
                    Level level = _doc.GetElement(host.LevelId) as Level;
                    FamilyInstance arHole = _doc.Create.NewFamilyInstance(
                        arPoint,
                        arSymbol,
                        host,
                        level,
                        StructuralType.NonStructural);

                    // 7. ПЕРЕНОС РАЗМЕРОВ
                    CopyParameters(mepHole, arHole, mapping, settings);
                }

                t.Commit();
            }
        }

        private void CopyParameters(FamilyInstance from, FamilyInstance to, MappingInfo mapping, HoleFamilyTypes settings)
        {
            if (mapping.IsRect)
            {
                // Для прямоугольных: берем имена параметров из JSON (Width, Height, Depth)
                var pNames = settings.FamilyTypes.Rectangled.FamilyParameters;

                // В МЕР-семействе: Width, Height, Depth
                // В АР-семействе: обычно те же или ADSK_Размер_Ширина и т.д.
                // ВНИМАНИЕ: Проверь имена параметров в АР семействах!
                to.LookupParameter("ADSK_Размер_Ширина")?.Set(from.LookupParameter("Width").AsDouble());
                to.LookupParameter("ADSK_Размер_Высота")?.Set(from.LookupParameter("Height").AsDouble());
                to.LookupParameter("ADSK_Размер_Глубина")?.Set(from.LookupParameter("Depth").AsDouble());
            }
            else
            {
                // Для круглых: Diameter, Depth
                to.LookupParameter("ADSK_Размер_Диаметр")?.Set(from.LookupParameter("Diameter").AsDouble());
                to.LookupParameter("ADSK_Размер_Глубина")?.Set(from.LookupParameter("Depth").AsDouble());
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
            ElementCategoryFilter catFilter = isWall ?
                new ElementCategoryFilter(BuiltInCategory.OST_Walls) :
                new ElementCategoryFilter(BuiltInCategory.OST_Floors);

            // Создаем небольшой объем для поиска (10 см вокруг точки)
            double tol = 0.3;
            Outline outline = new Outline(point - new XYZ(tol, tol, tol), point + new XYZ(tol, tol, tol));

            return new FilteredElementCollector(_doc)
                .WherePasses(catFilter)
                .WherePasses(new BoundingBoxIntersectsFilter(outline))
                .FirstOrDefault();
        }

        private FamilySymbol GetFamilySymbol(string familyName)
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(x => x.FamilyName == familyName);
        }
    }
}
