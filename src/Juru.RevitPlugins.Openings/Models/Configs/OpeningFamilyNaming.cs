using System;

namespace Juru.RevitPlugins.Openings.Models.Configs;

/// <summary>
/// Имена семейств заданий на отверстия и имена параметров внутри них.
/// </summary>
/// <remarks>
/// Семейства взяты из библиотеки Bim4Everyone и переименованы под нашу номенклатуру, поэтому имена
/// семейств наши, а имена параметров - их. Всё это лежит в настройках, а не в коде: переименование
/// семейства или параметра не должно требовать пересборки плагина.
/// </remarks>
public class OpeningFamilyNaming {
    /// <summary>
    /// Семейство прямоугольного отверстия в стене.
    /// </summary>
    public string WallRectangleFamily { get; set; } = "opening_wall_rect";

    /// <summary>
    /// Семейство круглого отверстия в стене.
    /// </summary>
    public string WallRoundFamily { get; set; } = "opening_wall_circ";

    /// <summary>
    /// Семейство прямоугольного отверстия в перекрытии.
    /// </summary>
    public string FloorRectangleFamily { get; set; } = "opening_slab_rect";

    /// <summary>
    /// Семейство круглого отверстия в перекрытии.
    /// </summary>
    public string FloorRoundFamily { get; set; } = "opening_slab_circ";

    /// <summary>
    /// Диаметр круглого отверстия.
    /// </summary>
    public string DiameterParam { get; set; } = "ADSK_Размер_Диаметр";

    /// <summary>
    /// Ширина прямоугольного отверстия.
    /// </summary>
    public string WidthParam { get; set; } = "ADSK_Размер_Ширина";

    /// <summary>
    /// Высота прямоугольного отверстия.
    /// </summary>
    public string HeightParam { get; set; } = "ADSK_Размер_Высота";

    /// <summary>
    /// Глубина отверстия - толщина конструкции в месте прохода.
    /// </summary>
    public string ThicknessParam { get; set; } = "ADSK_Размер_Глубина";

    /// <summary>
    /// Отметка оси отверстия от нуля здания.
    /// </summary>
    public string CenterOffsetParam { get; set; } = "ФОП_ВИС_Отметка оси от нуля";

    /// <summary>
    /// Отметка низа отверстия от нуля здания.
    /// </summary>
    public string BottomOffsetParam { get; set; } = "ФОП_ВИС_Отметка низа от нуля";

    /// <summary>
    /// Имя инженерной системы элемента, для которого выдано задание.
    /// </summary>
    public string MepSystemParam { get; set; } = "ФОП_ВИС_Имя системы";

    /// <summary>
    /// Описание задания: файлы, имена и id элементов, из пересечения которых оно возникло.
    /// </summary>
    public string DescriptionParam { get; set; } = "ФОП_Описание";

    /// <summary>
    /// Дата выдачи задания.
    /// </summary>
    public string DateParam { get; set; } = "ФОП_Дата";

    /// <summary>
    /// Автор задания.
    /// </summary>
    public string AuthorParam { get; set; } = "ФОП_Автор задания";

    /// <summary>
    /// Признак того, что задание размещено вручную и не должно перезаписываться авторасстановкой.
    /// </summary>
    public string ManuallyPlacedParam { get; set; } = "ФОП_Размещено вручную";

    /// <summary>
    /// Возвращает имя семейства для типа отверстия.
    /// </summary>
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
