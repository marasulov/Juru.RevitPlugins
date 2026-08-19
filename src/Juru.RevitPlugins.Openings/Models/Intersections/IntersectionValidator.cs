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
    /// <summary>
    /// Наименьшая доля объёма сечения, ниже которой пересечение считается касанием, а не проходом.
    /// </summary>
    private const double _minVolumeRatio = 0.05;

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
    /// Отклоняет пересечение, объём которого слишком мал по сравнению с сечением элемента:
    /// элемент задел конструкцию, но не проходит через неё.
    /// </summary>
    private string GetVolumeRejectionReason(MepStructureIntersection intersection) {
        double sectionArea = MepGeometry.GetSectionArea(intersection.Mep.Size);
        return sectionArea > 0 && intersection.Volume <= sectionArea * _minVolumeRatio
            ? "Объём пересечения слишком мал: элемент задевает конструкцию, но не проходит сквозь неё."
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
