using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Schedules.Models;

using Juru.RevitPlugins.Schedules.Localization;

namespace Juru.RevitPlugins.Schedules.Services.Categories;

/// <summary>
///     Названия и отделка помещений. Помещение нельзя перерисовать, как спецификацию: его название
///     печатают марки в видах и спецификации по всей модели, поэтому английский комплект получает его
///     только записью в параметры.
/// </summary>
public sealed class Rooms : TranslatableCategory
{
    public override string Key
    {
        get { return "Rooms"; }
    }

    public override string Name
    {
        get { return Text.CategoryRooms; }
    }

    public override string EmptyNote
    {
        get { return Text.EmptyRooms; }
    }

    public override IReadOnlyList<ElementText> Collect(CollectScope scope)
    {
        var rooms = scope.Instances(BuiltInCategory.OST_Rooms);

        return Order(rooms.SelectMany(room => DescribeAll(
            room,
            SpatialContext.Describe(room),
            TextTarget.Name(),
            TextTarget.Parameter(BuiltInParameter.ROOM_DEPARTMENT, Text.TargetDepartment),
            TextTarget.Parameter(BuiltInParameter.ROOM_OCCUPANCY, Text.TargetOccupancy),
            TextTarget.Parameter(BuiltInParameter.ROOM_FINISH_FLOOR, Text.TargetFinishFloor),
            TextTarget.Parameter(BuiltInParameter.ROOM_FINISH_WALL, Text.TargetFinishWall),
            TextTarget.Parameter(BuiltInParameter.ROOM_FINISH_CEILING, Text.TargetFinishCeiling),
            TextTarget.Parameter(BuiltInParameter.ROOM_FINISH_BASE, Text.TargetFinishBase),
            TextTarget.Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, Text.TargetComments))));
    }
}

/// <summary>
///     Названия зон и схем зонирования. Зона хранит имя в том же параметре, что помещение.
/// </summary>
public sealed class Areas : TranslatableCategory
{
    public override string Key
    {
        get { return "Areas"; }
    }

    public override string Name
    {
        get { return Text.CategoryAreas; }
    }

    public override string EmptyNote
    {
        get { return Text.EmptyAreas; }
    }

    public override IReadOnlyList<ElementText> Collect(CollectScope scope)
    {
        var areas = scope
            .Instances(BuiltInCategory.OST_Areas)
            .SelectMany(area => DescribeAll(
                area,
                SpatialContext.Describe(area),
                TextTarget.Name(),
                TextTarget.Parameter(BuiltInParameter.ROOM_DEPARTMENT, Text.TargetDepartment),
                TextTarget.Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, Text.TargetComments)));

        // Схема зонирования печатается в заголовках, поэтому переводится вместе с зонами.
        // Схема зонирования вида не имеет: она общая на модель.
        var schemes = scope.WholeModel()
            .OfClass(typeof(AreaScheme))
            .SelectMany(scheme => DescribeAll(scheme, Text.ContextAreaScheme, TextTarget.Name()));

        return Order(areas.Concat(schemes));
    }
}

/// <summary>Где искать помещение или зону: уровень и номер, потому что названия повторяются десятками.</summary>
internal static class SpatialContext
{
    public static string Describe(Element element)
    {
        var number = element.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString()?.Trim() ?? string.Empty;
        var level = element.Document.GetElement(element.LevelId)?.Name ?? string.Empty;

        if (number.Length is 0)
        {
            return level;
        }

        return level.Length is 0 ? number : $"{level} / {number}";
    }
}
