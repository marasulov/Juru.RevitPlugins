using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Core.FileNaming;
using Juru.RevitPlugins.Openings.Models.Configs;
using Juru.RevitPlugins.Openings.Models.Navigator;
using Juru.RevitPlugins.Openings.Models.Placement;
using Juru.RevitPlugins.Openings.Models.Server.Dto;

namespace Juru.RevitPlugins.Openings.Models.Server;

/// <summary>
/// Собирает пачку выданных заданий из активного файла - все задания, размещённые сейчас в файле,
/// не только выставленные последним запуском расстановки.
/// </summary>
public class OpeningBatchBuilder {
    private readonly Document _doc;
    private readonly OpeningsConfig _config;

    public OpeningBatchBuilder(Document doc, OpeningsConfig config) {
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// Собирает пачку. Задания нестандартного семейства (<see cref="OpeningFamilyNaming.SlabUniqueFamily"/>)
    /// в неё не попадают - у них нет типа отверстия, контракт сервера их не описывает - и
    /// возвращаются отдельно в <paramref name="skipped"/>, чтобы команда могла показать их число.
    /// </summary>
    public OpeningBatchDto Build(
        RevitFileNameInfo fileInfo,
        string scope,
        out IReadOnlyCollection<OpeningTask> skipped) {

        if(fileInfo is null) {
            throw new ArgumentNullException(nameof(fileInfo));
        }

        var tasks = new OpeningTasksProvider(_doc, _config).GetTasks();
        var skippedList = new List<OpeningTask>();
        var items = new List<OpeningItemDto>();

        var projectBasePoint = BasePoint.GetProjectBasePoint(_doc).Position;

        foreach(var task in tasks) {
            if(task.OpeningType is null) {
                skippedList.Add(task);
                continue;
            }

            items.Add(ToItem(task, task.OpeningType.Value, projectBasePoint));
        }

        skipped = skippedList;
        return new OpeningBatchDto {
            ProjectCode = fileInfo.ProjectCode,
            ObjectName = fileInfo.ObjectName,
            Discipline = fileInfo.Discipline,
            SentBy = WindowsUser.Name,
            SentByDisplayName = WindowsUser.FullName,
            SentAt = DateTime.UtcNow,
            WipFilePath = _doc.PathName,
            Scope = scope,
            Items = items,
            CoordinateFingerprint = CoordinateFingerprintReader.Get(_doc)
        };
    }

    private OpeningItemDto ToItem(OpeningTask task, OpeningType openingType, XYZ projectBasePoint) {
        var location = task.Location ?? XYZ.Zero;
        var relative = location - projectBasePoint;
        var size = task.GetSize();

        return new OpeningItemDto {
            TaskKey = task.UniqueId,
            OpeningType = openingType,
            Location = ToMeters(relative),
            WidthMm = task.IsRound ? null : ToMillimeters(size.Width),
            HeightMm = task.IsRound ? null : ToMillimeters(size.Height),
            DiameterMm = task.IsRound ? ToMillimeters(size.Diameter) : null,
            ThicknessMm = ToMillimeters(size.Thickness),
            RotationRad = OpeningAngle.GetCurrent(task.Frame),
            MepSystem = task.MepSystem,
            // Вид инженерного элемента (труба/воздуховод/лоток) на экземпляре задания не хранится -
            // его знает только расстановка в момент пересечения. Пусто, а не угадано: сервер должен
            // получать null, а не первый по счёту MepKind. См. заметку в памяти проекта.
            MepElementKind = null,
            HostKind = task.IsInWall ? StructureKind.Wall : StructureKind.Floor,
            LevelName = GetLevelName(task.Instance),
            ElevationFromZeroMm = ToMillimeters(task.GetElevation()),
            Description = task.Description,
            ManuallyPlaced = task.IsManuallyPlaced,
            IsUnited = task.IsUnited
        };
    }

    private string GetLevelName(FamilyInstance instance) {
        return _doc.GetElement(instance.LevelId) is Level level ? level.Name : string.Empty;
    }

    private static OpeningLocationDto ToMeters(XYZ feet) {
        return new OpeningLocationDto {
            X = UnitUtils.ConvertFromInternalUnits(feet.X, UnitTypeId.Meters),
            Y = UnitUtils.ConvertFromInternalUnits(feet.Y, UnitTypeId.Meters),
            Z = UnitUtils.ConvertFromInternalUnits(feet.Z, UnitTypeId.Meters)
        };
    }

    private static double ToMillimeters(double feet) {
        return UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);
    }
}
