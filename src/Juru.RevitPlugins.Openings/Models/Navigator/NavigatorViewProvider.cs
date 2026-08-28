using System.Linq;

using Autodesk.Revit.DB;

namespace Juru.RevitPlugins.Openings.Models.Navigator;

/// <summary>
/// Находит или создаёт отдельный 3D-вид навигатора: клики "Показать" переключают на него,
/// а не на текущий вид пользователя, - так раскраска и подсветка хоста не портят рабочий вид.
/// </summary>
/// <remarks>
/// Один вид на пользователя и на направление (исходящие/входящие задания), как у Bim4Everyone
/// (<c>BIM_Задания на отверстия_{username}</c>) - общий вид на всех означал бы, что раскраска
/// одного пользователя ломает вид другого при одновременной работе.
/// </remarks>
public static class NavigatorViewProvider {
    public static View3D GetOrCreate(Document doc, string suffix) {
        string name = $"BIM_Задания на отверстия_{suffix}_{doc.Application.Username}";

        var existing = new FilteredElementCollector(doc)
            .OfClass(typeof(View3D))
            .Cast<View3D>()
            .FirstOrDefault(view => !view.IsTemplate && view.Name == name);
        if(existing is not null) {
            return existing;
        }

        var viewFamilyType = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .First(type => type.ViewFamily == ViewFamily.ThreeDimensional);

        using var transaction = new Transaction(doc, "Создание вида навигатора заданий на отверстия");
        transaction.Start();

        var view = View3D.CreateIsometric(doc, viewFamilyType.Id);
        view.Name = name;
        view.DetailLevel = ViewDetailLevel.Fine;
        view.DisplayStyle = DisplayStyle.HLR;
        view.IsSectionBoxActive = false;

        transaction.Commit();
        return view;
    }
}
