using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Openings.Models.Configs;

namespace Juru.RevitPlugins.Openings.Models.Intersections;

/// <summary>
/// Отбирает пересечения, для которых имеет смысл выдавать задание на отверстие.
/// </summary>
/// <remarks>
/// Отклонённые пересечения не выбрасываются, а возвращаются с причиной: инженер должен видеть,
/// почему в этом месте задания нет, иначе отсутствие отверстия выглядит как ошибка плагина.
/// </remarks>
public class IntersectionValidator {
    private readonly double _minPassDepth;

    /// <param name="config">
    /// Настройки: из них берётся наименьшая глубина, при которой пересечение считается проходом.
    /// </param>
    public IntersectionValidator(OpeningsConfig config) {
        if(config is null) { throw new ArgumentNullException(nameof(config)); }

        _minPassDepth = UnitUtils.ConvertToInternalUnits(config.MinPassDepthMm, UnitTypeId.Millimeters);
    }

    /// <summary>
    /// Делит пересечения на те, для которых нужно выдать задание, и отклонённые с причинами.
    /// </summary>
    public ValidationResult Validate(IReadOnlyCollection<MepStructureIntersection> intersections) {
        if(intersections is null) { throw new ArgumentNullException(nameof(intersections)); }

        List<MepStructureIntersection> accepted = [];
        List<IntersectionRejection> rejected = [];

        foreach(var intersection in intersections) {
            string reason = GetRejectionReason(intersection);
            if(reason is null) {
                accepted.Add(intersection);
            } else {
                rejected.Add(new IntersectionRejection(intersection, reason));
            }
        }

        return new ValidationResult(accepted, rejected);
    }

    private string GetRejectionReason(MepStructureIntersection intersection) {
        if(intersection.GeometryIsInvalid) {
            return "Не удалось вычислить геометрию пересечения, габариты отверстия неизвестны.";
        }

        return intersection.StructureKind == StructureKind.Wall
            ? GetWallRejectionReason(intersection)
            : GetFloorRejectionReason(intersection);
    }

    private string GetWallRejectionReason(MepStructureIntersection intersection) {
        if(intersection.StructureElement is not Wall wall) {
            return "Конструкция не является стеной.";
        }

        if(wall.WallType?.Kind == WallKind.Curtain) {
            return "Витражная стена: отверстия в ней не размещаются.";
        }

        if(wall.Location is not LocationCurve location || location.Curve is not Line) {
            return "Криволинейная стена: расчёт габаритов отверстия не поддерживается.";
        }

        if(!intersection.Mep.IsFitting && MepGeometry.IsVertical(intersection.Mep.Element)) {
            return "Вертикальный элемент в стене: проход по вертикали стену не пересекает насквозь.";
        }

        string parallelReason = GetParallelRejectionReason(intersection, wall);
        return parallelReason ?? GetVolumeRejectionReason(intersection);
    }

    private string GetFloorRejectionReason(MepStructureIntersection intersection) {
        return !intersection.Mep.IsFitting && MepGeometry.IsHorizontal(intersection.Mep.Element)
            ? "Горизонтальный элемент в перекрытии: проход по горизонтали перекрытие не пересекает насквозь."
            : GetVolumeRejectionReason(intersection);
    }

    /// <summary>
    /// Отклоняет элемент, идущий вдоль стены: он пересекает её, но проходом сквозь неё не является.
    /// </summary>
    private string GetParallelRejectionReason(MepStructureIntersection intersection, Wall wall) {
        var direction = MepGeometry.GetDirection(intersection.Mep.Element);
        if(direction is null) {
            return null;
        }

        var wallNormal = intersection.StructureProvider.MainTransform.OfVector(wall.Orientation);
        double angleToNormal = direction.AngleTo(wallNormal);
        // Ось элемента перпендикулярна нормали стены - значит элемент идёт вдоль стены.
        return Math.Abs(angleToNormal - Math.PI / 2) < 0.01
            ? "Элемент идёт вдоль стены, а не сквозь неё."
            : null;
    }

    /// <summary>
    /// Отклоняет пересечение, слишком мелкое, чтобы считаться проходом: элемент задел конструкцию,
    /// но сквозь неё не идёт.
    /// </summary>
    /// <remarks>
    /// Объём пересечения делится на площадь сечения элемента, и получается средняя глубина прохода.
    /// Так порог задаётся в миллиметрах, а не долей объёма: инженеру понятно, что значит
    /// "проход глубиной меньше 15 мм", и непонятно, что значит "меньше 5% сечения".
    /// Ноль в настройках отключает проверку - тогда задания появятся и на касаниях.
    /// </remarks>
    private string GetVolumeRejectionReason(MepStructureIntersection intersection) {
        if(_minPassDepth <= 0) {
            return null;
        }

        double sectionArea = MepGeometry.GetSectionArea(intersection.Mep.Size);
        return sectionArea > 0 && intersection.Volume / sectionArea <= _minPassDepth
            ? "Проход слишком мелкий: элемент задевает конструкцию, но не проходит сквозь неё."
            : null;
    }

    /// <summary>
    /// Результат отбора пересечений.
    /// </summary>
    public class ValidationResult {
        public ValidationResult(
            IReadOnlyCollection<MepStructureIntersection> accepted,
            IReadOnlyCollection<IntersectionRejection> rejected) {
            Accepted = accepted;
            Rejected = rejected;
        }

        /// <summary>
        /// Пересечения, для которых нужно выдать задание.
        /// </summary>
        public IReadOnlyCollection<MepStructureIntersection> Accepted { get; }

        /// <summary>
        /// Отклонённые пересечения с причинами.
        /// </summary>
        public IReadOnlyCollection<IntersectionRejection> Rejected { get; }

        /// <summary>
        /// Причины отказа с количеством пересечений по каждой - для сводки в отчёте.
        /// </summary>
        public IReadOnlyCollection<KeyValuePair<string, int>> GetReasonSummary() {
            return Rejected
                .GroupBy(rejection => rejection.Reason)
                .Select(group => new KeyValuePair<string, int>(group.Key, group.Count()))
                .OrderByDescending(pair => pair.Value)
                .ToArray();
        }
    }
}
