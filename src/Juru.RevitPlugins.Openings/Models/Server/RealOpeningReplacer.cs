using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

using Juru.RevitPlugins.Openings.Models.Configs;
using Juru.RevitPlugins.Openings.Models.Placement;
using Juru.RevitPlugins.Openings.Models.Server.Dto;

namespace Juru.RevitPlugins.Openings.Models.Server;

/// <summary>
/// Заменяет семейство задания на вырезающее для тех заданий, что согласовала своя дисциплина,
/// и возвращает обратно, если решение сбросили или отклонили.
/// </summary>
/// <remarks>
/// Прорезание (<see cref="PlaceApproved"/>) - действие, отдельно запрашиваемое пользователем
/// кнопками "Вставить"/"Вставить все согласованные" в навигаторе, а не побочный эффект обновления:
/// это необратимое (в рамках навигатора) изменение модели, реши прорезать сколько-то заданий
/// пользователь должен сам. Откат (<see cref="RevertRejected"/>), наоборот, безопасная уборка -
/// он выполняется автоматически при каждом обновлении: сбросить решение и оставить чистовое
/// отверстие "прорезанным по недействующему решению" было бы хуже, чем тихо убрать его самим.
/// Запускаются оба после <see cref="IncomingOpeningPlacer.Place"/> - тот всегда ставит/сверяет
/// экземпляр-задание по каждой не удалённой задаче, независимо от согласования, поэтому к моменту
/// вызова откат находит экземпляр-задание уже на месте (создан заново, раз старый был удалён при
/// прежнем прорезании) - откат сводится к тому, чтобы убрать чистовое отверстие, а не пересоздавать
/// задание вручную.
/// </remarks>
public class RealOpeningReplacer {
    private readonly Document _doc;
    private readonly OpeningsConfig _config;
    private readonly Dictionary<OpeningType, FamilySymbol> _symbols = [];

    public RealOpeningReplacer(Document doc, OpeningsConfig config) {
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// Убирает чистовые отверстия, чьё согласование сбросили или отклонили, и сообщает о тех,
    /// что уже прорезаны и остаются в силе - навигатору нужно и то, и другое: одно для очистки,
    /// другое для показа/выделения актуального экземпляра. Ничего не создаёт - безопасно
    /// вызывать при каждом обновлении.
    /// </summary>
    public RealOpeningResult RevertRejected(IReadOnlyCollection<OpeningTaskViewDto> tasks) {
        if(tasks is null) { throw new ArgumentNullException(nameof(tasks)); }

        var result = new RealOpeningResult();
        var realByTaskKey = GetExistingRealInstances();

        using var transaction = new Transaction(_doc, "Возврат отклонённых отверстий в задание");
        transaction.Start();

        foreach(var task in tasks) {
            if(!realByTaskKey.TryGetValue(task.TaskKey, out var real)) {
                continue;
            }

            // Удалённое задание (инженер убрал трубу) чистит IncomingOpeningPlacer, но только
            // экземпляр-задание - тот, что заменён на вырезающее семейство, IncomingOpeningPlacer
            // не находит (у него нет TaskKeyParam с этим ключом среди экземпляров-заданий,
            // ключ только на вырезающем). Само отверстие здесь не удаляется: конструкцию уже
            // прорезали по согласованному заданию, и молча зарастить её обновлением окна нельзя -
            // навигатор показывает такое отдельной строкой, а убирает его человек
            // (docs/OpeningsApi.md, "Удалённые задания").
            if(task.Deleted) {
                result.DeletedRealElements[task.TaskKey] = real.Id;
                continue;
            }

            // Прорезание требует общего статуса "approved" (обе дисциплины), а не решения только
            // своей - иначе отклонение другой стороны никак не убирало бы уже прорезанное
            // отверстие, вставленное до того, как отклонили (rejected побеждает по контракту).
            if(OpeningApproval.IsApproved(task)) {
                result.RealElements[task.TaskKey] = real.Id;
            } else {
                _doc.Delete(real.Id);
                result.Reverted++;
            }
        }

        transaction.Commit();
        return result;
    }

    /// <summary>
    /// Прорезает вырезающим семейством те задания из <paramref name="tasks"/>, что согласовала
    /// своя дисциплина и что ещё не прорезаны - по кнопке "Вставить"/"Вставить все согласованные".
    /// <paramref name="placedElements"/> - результат последнего <see cref="IncomingOpeningPlacer.Place"/>
    /// (ключ задания -> экземпляр-задание), из которого берётся точка, поворот и уровень.
    /// </summary>
    public RealOpeningResult PlaceApproved(
        IReadOnlyCollection<OpeningTaskViewDto> tasks,
        IReadOnlyDictionary<string, ElementId> placedElements) {

        if(tasks is null) { throw new ArgumentNullException(nameof(tasks)); }
        if(placedElements is null) { throw new ArgumentNullException(nameof(placedElements)); }

        var result = new RealOpeningResult();
        var realByTaskKey = GetExistingRealInstances();

        using var transaction = new Transaction(_doc, "Прорезание согласованных отверстий");
        transaction.Start();

        foreach(var task in tasks) {
            // Общий статус - обе дисциплины должны согласовать (contract), не только своя.
            if(task.Deleted) {
                continue;
            }

            if(!OpeningApproval.IsApproved(task)) {
                result.SkippedNotApproved++;
                continue;
            }

            if(realByTaskKey.TryGetValue(task.TaskKey, out var real)) {
                // Уже прорезано - идемпотентно, геометрию заново не сверяем: если задание
                // сдвинулось после согласования, сервер сам сбрасывает решение в "sent"
                // (docs/OpeningsApi.md), и RevertRejected на следующем обновлении откатит.
                result.RealElements[task.TaskKey] = real.Id;
                continue;
            }

            if(!placedElements.TryGetValue(task.TaskKey, out var previewId)) {
                result.SkippedNoTaskInstance++;
                continue;
            }

            var preview = _doc.GetElement(previewId) as FamilyInstance;
            if(preview?.Location is not LocationPoint location) {
                result.SkippedNoTaskInstance++;
                continue;
            }

            if(CreateReal(task, preview, location.Point, result) is FamilyInstance created) {
                _doc.Delete(preview.Id);
                result.RealElements[task.TaskKey] = created.Id;
                result.Placed++;
            }
        }

        transaction.Commit();
        return result;
    }

    private FamilyInstance CreateReal(
        OpeningTaskViewDto task,
        FamilyInstance preview,
        XYZ point,
        RealOpeningResult result) {

        var symbol = GetSymbol(task.OpeningType);
        if(symbol is null) {
            result.MissingFamilies.Add(_config.RealNaming.GetFamilyName(task.OpeningType));
            return null;
        }

        var host = HostFinder.Find(_doc, point, task.OpeningType);
        if(host is null) {
            // Вырезающее семейство хостовое: без стены или перекрытия под точкой вставки его
            // не создать. Молчать об этом нельзя - задание согласовано, а отверстия не появилось.
            result.SkippedNoHost++;
            return null;
        }

        if(!symbol.IsActive) {
            symbol.Activate();
            _doc.Regenerate();
        }

        var level = _doc.GetElement(preview.LevelId) as Level;
        var instance = _doc.Create.NewFamilyInstance(point, symbol, host, level, StructuralType.NonStructural);
        _doc.Regenerate();

        if(Math.Abs(task.RotationRad) > 1e-9) {
            var axis = Line.CreateBound(point, point + XYZ.BasisZ);
            ElementTransformUtils.RotateElement(_doc, instance.Id, axis, task.RotationRad);
        }

        WriteParameters(instance, task);
        return instance;
    }

    private void WriteParameters(FamilyInstance instance, OpeningTaskViewDto task) {
        var naming = _config.RealNaming;
        bool isRound = task.OpeningType is OpeningType.WallRound or OpeningType.FloorRound;
        if(isRound) {
            ParameterWriter.SetLength(instance, naming.DiameterParam, ToFeet(task.DiameterMm ?? 0));
        } else {
            ParameterWriter.SetLength(instance, naming.WidthParam, ToFeet(task.WidthMm ?? 0));
            ParameterWriter.SetLength(instance, naming.HeightParam, ToFeet(task.HeightMm ?? 0));
        }
        ParameterWriter.SetLength(instance, naming.ThicknessParam, ToFeet(task.ThicknessMm));
        // Ключ задания - в готовый параметр Bim4Everyone: своих параметров у этого семейства нет,
        // а без ключа следующий запуск не отличил бы "уже прорезано" от "прорезать заново".
        ParameterWriter.SetText(instance, naming.TaskKeyParam, task.TaskKey);
    }

    /// <summary>
    /// Индексирует уже вырезанные чистовые отверстия по ключу задания.
    /// </summary>
    private Dictionary<string, FamilyInstance> GetExistingRealInstances() {
        var naming = _config.RealNaming;
        var familyNames = new HashSet<string>(
            [naming.WallRectangleFamily, naming.WallRoundFamily, naming.FloorRectangleFamily, naming.FloorRoundFamily],
            StringComparer.CurrentCultureIgnoreCase);

        var byTaskKey = new Dictionary<string, FamilyInstance>();
        var instances = new FilteredElementCollector(_doc)
            .OfClass(typeof(FamilyInstance))
            .WhereElementIsNotElementType()
            .Cast<FamilyInstance>()
            .Where(instance => instance.Symbol is not null && familyNames.Contains(instance.Symbol.FamilyName));

        foreach(var instance in instances) {
            string taskKey = instance.LookupParameter(naming.TaskKeyParam)?.AsString();
            if(!string.IsNullOrWhiteSpace(taskKey)) {
                byTaskKey[taskKey] = instance;
            }
        }

        return byTaskKey;
    }

    private FamilySymbol GetSymbol(OpeningType openingType) {
        if(_symbols.TryGetValue(openingType, out var cached)) {
            return cached;
        }

        string familyName = _config.RealNaming.GetFamilyName(openingType);
        var symbol = new FilteredElementCollector(_doc)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .FirstOrDefault(item => item.FamilyName.Equals(familyName, StringComparison.CurrentCultureIgnoreCase));

        _symbols[openingType] = symbol;
        return symbol;
    }

    private static double ToFeet(double millimeters) {
        return UnitUtils.ConvertToInternalUnits(millimeters, UnitTypeId.Millimeters);
    }
}
