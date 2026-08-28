using System;

namespace Juru.RevitPlugins.Openings.Models.Configs;

/// <summary>
/// Имена вырезающих семейств чистовых отверстий и параметров внутри них - те же семейства
/// Bim4Everyone, без переименования (в отличие от семейств заданий, docs/OpeningTaskFamilies.md).
/// </summary>
/// <remarks>
/// Лежит в настройках, а не в коде: пользователь предупредил, что может переименовать эти семейства
/// на английский вариант позже - переименование не должно требовать пересборки плагина.
/// <c>opening_slab_unique</c> сюда не входит - его плагин не считает и не заменяет
/// (см. <c>OpeningFamilyNaming.SlabUniqueFamily</c>).
/// </remarks>
public class RealOpeningFamilyNaming {
    public string WallRectangleFamily { get; set; } = "Окн_Отв_Прямоуг_Стена";

    public string WallRoundFamily { get; set; } = "Окн_Отв_Круг_Стена";

    public string FloorRectangleFamily { get; set; } = "Окн_Отв_Прямоуг_Перекрытие";

    public string FloorRoundFamily { get; set; } = "Окн_Отв_Круг_Перекрытие";

    public string DiameterParam { get; set; } = "ФОП_РАЗМ_Диаметр";

    public string WidthParam { get; set; } = "ФОП_РАЗМ_Ширина проёма";

    public string HeightParam { get; set; } = "ФОП_РАЗМ_Высота проёма";

    public string ThicknessParam { get; set; } = "ФОП_РАЗМ_Глубина проёма";

    /// <summary>
    /// Ключ задания на экземпляре чистового отверстия - используется вместо
    /// <see cref="OpeningFamilyNaming.TaskKeyParam"/>: у вырезающего семейства своих параметров нет,
    /// поэтому ключ пишется в готовый параметр Bim4Everyone, который по смыслу для этого и был.
    /// </summary>
    public string TaskKeyParam { get; set; } = "ФОП_ID задания";

    public string GetFamilyName(OpeningType openingType) {
        switch(openingType) {
            case OpeningType.WallRectangle:
                return WallRectangleFamily;
            case OpeningType.WallRound:
                return WallRoundFamily;
            case OpeningType.FloorRectangle:
                return FloorRectangleFamily;
            case OpeningType.FloorRound:
                return FloorRoundFamily;
            default:
                throw new ArgumentOutOfRangeException(nameof(openingType));
        }
    }
}
