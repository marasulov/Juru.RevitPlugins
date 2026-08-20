using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Openings.Models.Configs;

namespace Juru.RevitPlugins.Openings.Models.Placement;

/// <summary>
/// Задания на отверстия, уже размещённые в файле. Нужен, чтобы повторный запуск не плодил дубли.
/// </summary>
/// <remarks>
/// Опознание идёт по точке вставки и объёму отверстия, как в <c>IsAlreadyPlaced</c> у Bim4Everyone,
/// но с более широким допуском по точке: у них расстановка воспроизводит точку до миллиметра, а у нас
/// габариты округляются вверх, и точка круглого отверстия зависит от округлённого габарита.
/// Задания, размещённые вручную, считаются занятыми местами при любом размере: их авторасстановка
/// не должна ни перекрывать, ни дублировать.
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
            _tasks.Add(new PlacedTask(location.Point, GetVolume(instance), manuallyPlaced));
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
    /// Добавляет только что размещённое задание, чтобы дубли не появились внутри одного прогона.
    /// </summary>
    public void Add(XYZ point, double volume) {
        _tasks.Add(new PlacedTask(point, volume, manuallyPlaced: false));
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

        public PlacedTask(XYZ point, double volume, bool manuallyPlaced) {
            _point = point;
            _volume = volume;
            _manuallyPlaced = manuallyPlaced;
        }

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
