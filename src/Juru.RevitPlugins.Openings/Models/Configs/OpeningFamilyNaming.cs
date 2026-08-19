using System;

namespace Juru.RevitPlugins.Openings.Models.Configs;

/// <summary>
/// Имена семейств заданий на отверстия и имена параметров внутри них.
/// </summary>
/// <remarks>
/// Семейства взяты из библиотеки Bim4Everyone и переименованы под нашу номенклатуру. Габариты остались
/// на стандартных параметрах ADSK, а те параметры, что несли чужие обозначения - деление на ВИС и
/// авторство задания, - заведены заново с префиксом Juru. Всё это лежит в настройках, а не в коде:
/// переименование не должно требовать пересборки плагина.
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
    public string CenterOffsetParam { get; set; } = "Juru_Opening_CenterElevation";

    /// <summary>
    /// Отметка низа отверстия от нуля здания.
    /// </summary>
    public string BottomOffsetParam { get; set; } = "Juru_Opening_BottomElevation";

    /// <summary>
    /// Имя инженерной системы элемента, для которого выдано задание.
    /// </summary>
    public string MepSystemParam { get; set; } = "Juru_Opening_System";

    /// <summary>
    /// Описание задания: файлы, имена и id элементов, из пересечения которых оно возникло.
    /// </summary>
    public string DescriptionParam { get; set; } = "Juru_Opening_Description";

    /// <summary>
    /// Дата выдачи задания.
    /// </summary>
    public string DateParam { get; set; } = "Juru_Opening_IssueDate";

    /// <summary>
    /// Автор задания.
    /// </summary>
    public string AuthorParam { get; set; } = "Juru_Opening_Author";

    /// <summary>
    /// Признак того, что задание размещено вручную и не должно перезаписываться авторасстановкой.
    /// </summary>
    public string ManuallyPlacedParam { get; set; } = "Juru_Opening_ManuallyPlaced";

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
