using System.Linq;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Openings.Models.Intersections;
using Juru.RevitPlugins.Openings.Models.Navigator;

namespace Juru.RevitPlugins.Openings.Models.Server;

/// <summary>
/// Пересчитывает "недопустимые конструкции" и "в разных конструкциях" для входящего задания
/// локально, по конструкциям самого файла АР/КР - без связи на инженера, которой в этой
/// архитектуре принципиально нет (см. "Недопустимые конструкции..." в <c>docs/OpeningsApi.md</c>).
/// </summary>
/// <remarks>
/// Та же логика, что у <see cref="OpeningTaskStatusUpdater"/> на стороне ВИС - там она сравнивает
/// солид задания с конструкциями из связи, здесь - с конструкциями активного документа напрямую.
/// "Объединённое" сюда не входит: это факт с ВИС-стороны, он приезжает флагом
/// (<see cref="Server.Dto.OpeningTaskViewDto.IsUnited"/>), геометрией на этой стороне не виден.
/// </remarks>
public static class IncomingTaskTechnicalCheck {
    private static readonly BuiltInCategory[] _unacceptableCategories = [
        BuiltInCategory.OST_StructuralColumns,
        BuiltInCategory.OST_StructuralFraming
    ];

    /// <summary>
    /// Возвращает состояние задания по локальной геометрии, либо <c>null</c>, если экземпляр
    /// не размещён или его солид не удалось получить - тогда показывать нечего.
    /// </summary>
    public static OpeningTaskStatus? Evaluate(Document doc, FamilyInstance? instance) {
        if(instance is null) {
            return null;
        }

        var solid = GeometryUtils.Unite(GeometryUtils.GetSolids(instance));
        if(solid is null) {
            return null;
        }

        if(Intersects(doc, solid, _unacceptableCategories)) {
            return OpeningTaskStatus.UnacceptableConstructions;
        }

        bool hitsWall = Intersects(doc, solid, [BuiltInCategory.OST_Walls]);
        bool hitsFloor = Intersects(doc, solid, [BuiltInCategory.OST_Floors]);
        if(hitsWall && hitsFloor) {
            return OpeningTaskStatus.DifferentConstructions;
        }

        return OpeningTaskStatus.Correct;
    }

    private static bool Intersects(Document doc, Solid solid, BuiltInCategory[] categories) {
        return new FilteredElementCollector(doc)
            .WherePasses(new ElementMulticategoryFilter(categories))
            .WhereElementIsNotElementType()
            .WherePasses(new BoundingBoxIntersectsFilter(GeometryUtils.GetOutline(solid)))
            .WherePasses(new ElementIntersectsSolidFilter(solid))
            .Any();
    }
}
