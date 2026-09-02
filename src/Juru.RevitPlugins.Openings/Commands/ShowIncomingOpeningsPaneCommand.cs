using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Juru.RevitPlugins.Core.Theming;
using Juru.RevitPlugins.Openings.Views.Navigator;

namespace Juru.RevitPlugins.Openings.Commands;

/// <summary>
/// Показывает закреплённую панель согласования: те же задания с сервера, что и в окне
/// (<see cref="ShowIncomingOpeningTasksCommand"/>), но решение принимается по одному заданию -
/// выделенному в списке, с переходом по списку кнопками.
/// </summary>
/// <remarks>
/// Только показывает: задания панель читает с сервера и ставит в модель по кнопке "Обновить"
/// в самой панели. Иначе загрузка шла бы и там, где её никто не просил, - Revit открывает
/// запомненные панели сам, при запуске, и на этом перестали открываться файлы.
/// Панель и окно - две независимые точки входа в одну работу: у каждой своё вью и своя модель
/// представления, состояние (выделение, непринятые решения) не общее. Окно эта команда не трогает.
/// </remarks>
[Transaction(TransactionMode.Manual)]
public class ShowIncomingOpeningsPaneCommand : IExternalCommand {
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) {
        if(IncomingOpeningsPane.RegistrationError is { } registrationError) {
            TaskDialog.Show("Согласование заданий",
                "Панель согласования не зарегистрирована: " + registrationError
                + "\n\nСогласовать задания можно в окне - кнопка \"Согласование заданий\".");
            return Result.Failed;
        }

        JuruTheme.Apply();
        commandData.Application.GetDockablePane(IncomingOpeningsPane.PaneId).Show();

        return Result.Succeeded;
    }
}
