using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Openings.Models.Configs;

namespace Juru.RevitPlugins.Openings.Models.Placement;

/// <summary>
/// Задания на отверстия, уже размещённые в файле. Нужен, чтобы повторный запуск не плодил дубли,
/// и чтобы сдвинутый инженерный элемент подвигал своё существующее задание, а не оставлял сироту
/// рядом с новым (см. <see cref="OpeningTaskPlacer.PlaceOne"/>).
/// </summary>
/// <remarks>
/// Опознание по точке и объёму идёт как в <c>IsAlreadyPlaced</c> у Bim4Everyone, но с более широким
/// допуском по точке: у них расстановка воспроизводит точку до миллиметра, а у нас габариты
/// округляются вверх, и точка круглого отверстия зависит от округлённого габарита.
/// Задания, размещённые вручную, считаются занятыми местами при любом размере: их авторасстановка
/// не должна ни перекрывать, ни дублировать, ни двигать.
/// </remarks>
public class PlacedTasksIndex {
    /// <summary>
    /// Допуск по точке вставки в мм.
    /// </summary>
    private const double _pointToleranceMm = 50;

    /// <summary>
    /// Допустимое относительное отклонение объёма отверстия.
    /// </summary>
    private const double _volumeTolerance = 0.2;

    private readonly List<PlacedTask> _tasks = [];
    private readonly Dictionary<int, PlacedTask> _byMepId = [];
    private readonly double _pointTolerance;

    public PlacedTasksIndex(Document doc, OpeningsConfig config) {
        if(doc is null) { throw new ArgumentNullException(nameof(doc)); }
        if(config is null) { throw new ArgumentNullException(nameof(config)); }

        _pointTolerance = UnitUtils.ConvertToInternalUnits(_pointToleranceMm, UnitTypeId.Millimeters);

        var familyNames = new HashSet<string>(
            Enum.GetValues(typeof(OpeningType))
                .Cast<OpeningType>()
                .Select(config.Naming.GetFamilyName)
                .Concat([config.Naming.SlabUniqueFamily]),
            StringComparer.CurrentCultureIgnoreCase);

        var instances = new FilteredElementCollector(doc)
            .OfClass(typeof(FamilyInstance))
            .WhereElementIsNotElementType()
            .Cast<FamilyInstance>()
            .Where(instance => instance.Symbol is not null
                && familyNames.Contains(instance.Symbol.FamilyName));

        foreach(var instance in instances) {
            if(instance.Location is not LocationPoint location) {
                continue;
            }

            bool manuallyPlaced = instance
                .LookupParameter(config.Naming.ManuallyPlacedParam)?.AsInteger() == 1;
            string description = instance
                .LookupParameter(config.Naming.DescriptionParam)?.AsString();
            var mepIds = OpeningSourceDescription.ParseMepElementIds(description);

            var task = new PlacedTask(instance, location.Point, GetVolume(instance), manuallyPlaced);
            _tasks.Add(task);

            // Ручное задание из опознания по инженерному элементу исключается - его не должна
            // подвинуть чужая расстановка, даже если инженерный элемент действительно тот же:
            // человек мог осознанно поставить его в другом месте под то же пересечение.
            if(!manuallyPlaced) {
                foreach(int mepId in mepIds) {
                    _byMepId.TryAdd(mepId, task);
                }
            }
        }
    }

    /// <summary>
    /// Сколько заданий уже стоит в файле.
    /// </summary>
    public int Count => _tasks.Count;

    /// <summary>
    /// Проверяет, стоит ли в этом месте такое же задание.
    /// </summary>
    /// <param name="point">Точка вставки нового задания.</param>
    /// <param name="volume">Объём нового отверстия в единицах Revit.</param>
    public bool AlreadyPlaced(XYZ point, double volume) {
        if(point is null) { throw new ArgumentNullException(nameof(point)); }

        return _tasks.Any(task => task.Matches(point, volume, _pointTolerance, _volumeTolerance));
    }

    /// <summary>
    /// Id элемента числом: в Revit 2024 <c>IntegerValue</c> сменился на <c>Value</c> типа long,
    /// а в 2026 старое свойство убрали совсем. Id элемента внутри файла в int укладывается -
    /// в описании задания он и записан числом.
    /// </summary>
    private static int GetIdValue(ElementId elementId) {
#if REVIT2024_OR_GREATER
        return (int) elementId.Value;
#else
        return elementId.IntegerValue;
#endif
    }

    /// <summary>
    /// Ищет уже стоящее задание того же инженерного элемента - тот самый проход, что и у нового
    /// отверстия, опознанный не по близости, а по id элемента, записанному в его описании при
    /// выдаче. Не возвращает задания, размещённые вручную - их эта расстановка не трогает.
    /// </summary>
    public FamilyInstance FindBySourceMep(IEnumerable<ElementId> mepElementIds) {
        foreach(var id in mepElementIds) {
            if(_byMepId.TryGetValue(GetIdValue(id), out var task)) {
                return task.Instance;
            }
        }

        return null;
    }

    /// <summary>
    /// Проверяет, совпадает ли уже стоящее задание того же элемента (см. <see cref="FindBySourceMep"/>)
    /// с новой точкой и объёмом достаточно, чтобы не двигать его - тот же допуск, что у
    /// <see cref="AlreadyPlaced"/>.
    /// </summary>
    public bool MatchesExisting(FamilyInstance instance, XYZ point, double volume) {
        var task = _tasks.FirstOrDefault(item => item.Instance.Id == instance.Id);
        return task is not null && task.Matches(point, volume, _pointTolerance, _volumeTolerance);
    }

    /// <summary>
    /// Убирает задание из индекса перед тем, как расстановка подвинет его на новое место -
    /// иначе оно осталось бы числиться на старой точке и мешало бы следующим пересечениям
    /// в этом же прогоне.
    /// </summary>
    public void Remove(FamilyInstance instance) {
        _tasks.RemoveAll(task => task.Instance.Id == instance.Id);
        foreach(var key in _byMepId.Where(pair => pair.Value.Instance.Id == instance.Id).Select(pair => pair.Key).ToList()) {
            _byMepId.Remove(key);
        }
    }

    /// <summary>
    /// Добавляет только что размещённое или подвинутое задание, чтобы дубли не появились внутри
    /// одного прогона.
    /// </summary>
    public void Add(FamilyInstance instance, XYZ point, double volume) {
        _tasks.Add(new PlacedTask(instance, point, volume, manuallyPlaced: false));
    }

    /// <summary>
    /// Возвращает объём отверстия по габаритам его экземпляра.
    /// </summary>
    private double GetVolume(FamilyInstance instance) {
        var box = instance.get_BoundingBox(null);
        if(box is null) {
            return 0;
        }

        return (box.Max.X - box.Min.X) * (box.Max.Y - box.Min.Y) * (box.Max.Z - box.Min.Z);
    }

    private class PlacedTask {
        private readonly XYZ _point;
        private readonly double _volume;
        private readonly bool _manuallyPlaced;

        public PlacedTask(FamilyInstance instance, XYZ point, double volume, bool manuallyPlaced) {
            Instance = instance;
            _point = point;
            _volume = volume;
            _manuallyPlaced = manuallyPlaced;
        }

        public FamilyInstance Instance { get; }

        public bool Matches(XYZ point, double volume, double pointTolerance, double volumeTolerance) {
            if(_point.DistanceTo(point) > pointTolerance) {
                return false;
            }

            // Ручное задание держит место независимо от размера: его нельзя ни перезаписать, ни продублировать.
            if(_manuallyPlaced) {
                return true;
            }

            if(_volume <= 0 || volume <= 0) {
                return true;
            }

            return Math.Abs(_volume - volume) / Math.Max(_volume, volume) <= volumeTolerance;
        }
    }
}
