using System;
using System.Collections.Generic;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Openings.Models.Configs;
using Juru.RevitPlugins.Openings.Models.Intersections;

namespace Juru.RevitPlugins.Openings.Models.Navigator;

/// <summary>
/// Задание на отверстие, размещённое в активном файле инженерных систем.
/// </summary>
/// <remarks>
/// Геометрия читается один раз и хранится в объекте: солид экземпляра семейства нужен почти каждой
/// проверке состояния, а обход геометрии - самая дорогая часть навигатора.
/// </remarks>
public class OpeningTask {
    private readonly OpeningFamilyNaming _naming;
    private Solid? _solid;
    private bool _solidRead;

    /// <param name="instance">Экземпляр семейства задания.</param>
    /// <param name="openingType">Тип отверстия, либо <c>null</c> для нестандартного семейства.</param>
    /// <param name="naming">Имена параметров в семействах заданий.</param>
    public OpeningTask(FamilyInstance instance, OpeningType? openingType, OpeningFamilyNaming naming) {
        Instance = instance ?? throw new ArgumentNullException(nameof(instance));
        _naming = naming ?? throw new ArgumentNullException(nameof(naming));
        OpeningType = openingType;
        Status = OpeningTaskStatus.Correct;
    }

    /// <summary>
    /// Экземпляр семейства задания.
    /// </summary>
    public FamilyInstance Instance { get; }

    public ElementId Id => Instance.Id;

    /// <summary>
    /// Неизменяемый идентификатор задания. Им же задание опознаётся на сервере согласования.
    /// </summary>
    public string UniqueId => Instance.UniqueId;

    /// <summary>
    /// Тип отверстия, либо <c>null</c> у нестандартного семейства, размещаемого вручную.
    /// </summary>
    public OpeningType? OpeningType { get; }

    /// <summary>
    /// Отверстие размещено в стене.
    /// </summary>
    public bool IsInWall => OpeningType is Configs.OpeningType.WallRectangle or Configs.OpeningType.WallRound;

    /// <summary>
    /// Отверстие круглое.
    /// </summary>
    public bool IsRound => OpeningType is Configs.OpeningType.WallRound or Configs.OpeningType.FloorRound;

    /// <summary>
    /// Состояние задания, пересчитанное по геометрии.
    /// </summary>
    public OpeningTaskStatus Status { get; set; }

    /// <summary>
    /// Конструкция из связи, в которой стоит задание. Может отсутствовать: задание вне конструкций
    /// тоже нужно показать.
    /// </summary>
    public Element? Host { get; set; }

    /// <summary>
    /// Инженерные элементы активного файла, проходящие через задание.
    /// </summary>
    public IReadOnlyCollection<Element> MepElements { get; set; } = [];

    /// <summary>
    /// Система координат задания: X вдоль конструкции, Y по её толщине, Z вверх.
    /// </summary>
    /// <remarks>
    /// Совпадает с системой координат конструкции, в которой задание размещено: стеновые семейства
    /// поворачиваются по нормали стены, поэтому их собственные оси и есть оси стены.
    /// </remarks>
    public Transform Frame => Instance.GetTotalTransform();

    /// <summary>
    /// Точка вставки задания в координатах активного файла.
    /// </summary>
    public XYZ? Location => (Instance.Location as LocationPoint)?.Point;

    /// <summary>
    /// Солид задания в координатах активного файла, либо <c>null</c>, если геометрии нет.
    /// </summary>
    public Solid? GetSolid() {
        if(_solidRead) {
            return _solid;
        }

        _solidRead = true;
        _solid = GeometryUtils.Unite(GeometryUtils.GetSolids(Instance));
        return _solid;
    }

    /// <summary>
    /// Имя инженерной системы, записанное в задание.
    /// </summary>
    public string MepSystem => GetText(_naming.MepSystemParam);

    /// <summary>
    /// Описание задания: элементы, из пересечения которых оно возникло.
    /// </summary>
    public string Description => GetText(_naming.DescriptionParam);

    /// <summary>
    /// Дата выдачи задания.
    /// </summary>
    public string Date => GetText(_naming.DateParam);

    /// <summary>
    /// Автор задания.
    /// </summary>
    public string Author => GetText(_naming.AuthorParam);

    /// <summary>
    /// Задание размещено вручную.
    /// </summary>
    public bool IsManuallyPlaced => GetFlag(_naming.ManuallyPlacedParam);

    /// <summary>
    /// Задание получено объединением нескольких проходов.
    /// </summary>
    public bool IsUnited => GetFlag(_naming.UnitedParam);

    /// <summary>
    /// Габариты задания в единицах Revit: диаметр либо ширина и высота, и глубина.
    /// </summary>
    public (double Width, double Height, double Diameter, double Thickness) GetSize() {
        return (
            GetLength(_naming.WidthParam),
            GetLength(_naming.HeightParam),
            GetLength(_naming.DiameterParam),
            GetLength(_naming.ThicknessParam));
    }

    /// <summary>
    /// Отметка низа задания в единицах Revit.
    /// </summary>
    public double GetBottomElevation() {
        return GetLength(_naming.BottomOffsetParam);
    }

    /// <summary>
    /// Отметка оси задания в единицах Revit.
    /// </summary>
    public double GetCenterElevation() {
        return GetLength(_naming.CenterOffsetParam);
    }

    private string GetText(string parameterName) {
        return Instance.LookupParameter(parameterName)?.AsString() ?? string.Empty;
    }

    private double GetLength(string parameterName) {
        var parameter = Instance.LookupParameter(parameterName);
        return parameter is null || !parameter.HasValue ? 0 : parameter.AsDouble();
    }

    private bool GetFlag(string parameterName) {
        return Instance.LookupParameter(parameterName)?.AsInteger() == 1;
    }
}
