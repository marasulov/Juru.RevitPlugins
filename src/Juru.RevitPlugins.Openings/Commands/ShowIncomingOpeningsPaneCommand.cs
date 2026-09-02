using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Juru.RevitPlugins.Core.Theming;
using Juru.RevitPlugins.Openings.Services;
using Juru.RevitPlugins.Openings.ViewModels.Navigator;
using Juru.RevitPlugins.Openings.Views.Navigator;

namespace Juru.RevitPlugins.Openings.Commands;

/// <summary>
/// Показывает закреплённую панель согласования: те же задания с сервера, что и в окне
/// (<see cref="ShowIncomingOpeningTasksCommand"/>), но решение принимается по одному заданию -
/// выделенному в списке, с переходом по списку кнопками.
/// </summary>
/// <remarks>
/// Панель и окно - две независимые точки входа в одну работу: у каждой своё вью и своя модель
/// представления, состояние (выделение, непринятые решения) не общее. Окно эта команда не трогает.
/// </remarks>
[Transaction(TransactionMode.Manual)]
public class ShowIncomingOpeningsPaneCommand : IExternalCommand {
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) {
        var uiApplication = commandData.Application;

        if(IncomingOpeningsPane.RegistrationError is { } registrationError) {
            TaskDialog.Show("Согласование заданий",
                "Панель согласования не зарегистрирована: " + registrationError
                + "\n\nСогласовать задания можно в окне - кнопка \"Согласование заданий\".");
            return Result.Failed;
        }

        JuruTheme.Apply();

        var pane = uiApplication.GetDockablePane(IncomingOpeningsPane.PaneId);

        // Панель уже собрана и с заданиями - показываем как есть: кнопку ленты нажимают и просто
        // чтобы вернуть её на экран, а пересборка потеряла бы решения, отмеченные но ещё не
        // отправленные. Перечитать задания с сервера - кнопка "Обновить" в самой панели.
        if(IncomingOpeningsPane.DataContext is IncomingOpeningsPaneViewModel loaded && loaded.Tasks.Count > 0) {
            pane.Show();
            return Result.Succeeded;
        }

        // Проверки файла и адреса сервера - внутри загрузчика: панель просит у него загрузку и сама,
        // когда Revit открыл её при запуске. Разница только в том, что причину отказа команда,
        // в отличие от той автозагрузки, объясняет пользователю.
        string error = IncomingOpeningsPaneLoader.Load(uiApplication);
        if(error is not null) {
            TaskDialog.Show("Согласование заданий", error);
            return Result.Cancelled;
        }

        pane.Show();

        return Result.Succeeded;
    }
}
