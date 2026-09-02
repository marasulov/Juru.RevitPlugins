using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Juru.RevitPlugins.Core.FileNaming;
using Juru.RevitPlugins.Core.Theming;
using Juru.RevitPlugins.Openings.Models.Configs;
using Juru.RevitPlugins.Openings.Models.Server;
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
        var doc = uiApplication.ActiveUIDocument.Document;
        var config = OpeningsConfig.GetOpeningsConfig();

        if(!RevitFileNameInfo.TryParse(doc.PathName, out var fileInfo)) {
            TaskDialog.Show("Согласование заданий",
                "Имя файла не соответствует стандарту наименования "
                + "(Проект-Объект-Дисциплина-vNN): без него нельзя определить проект и объект "
                + "для сервера.");
            return Result.Cancelled;
        }

        if(!fileInfo.Discipline.ReceivesOpeningTasks()) {
            TaskDialog.Show("Согласование заданий",
                "Дисциплина файла - " + fileInfo.Discipline
                + ". Согласование работает в файлах ARC и STR.");
            return Result.Cancelled;
        }

        string serverUrl = OpeningsServerOptions.ResolveUrl(config);
        if(string.IsNullOrWhiteSpace(serverUrl)) {
            TaskDialog.Show("Согласование заданий",
                "Адрес сервера не задан ни в настройках плагина, ни в ClashServer.json рядом со сборкой.");
            return Result.Cancelled;
        }

        if(IncomingOpeningsPane.RegistrationError is { } registrationError) {
            TaskDialog.Show("Согласование заданий",
                "Панель согласования не зарегистрирована: " + registrationError
                + "\n\nСогласовать задания можно в окне - кнопка \"Согласование заданий\".");
            return Result.Failed;
        }

        JuruTheme.Apply();

        var pane = uiApplication.GetDockablePane(IncomingOpeningsPane.PaneId);

        // Панель уже собрана - показываем как есть: кнопку ленты нажимают и просто чтобы вернуть
        // её на экран, а пересборка потеряла бы решения, отмеченные но ещё не отправленные.
        // Перечитать задания с сервера - кнопка "Обновить" в самой панели.
        if(IncomingOpeningsPane.DataContext is IncomingOpeningsPaneViewModel) {
            pane.Show();
            return Result.Succeeded;
        }

        var client = new OpeningsServerClient(serverUrl);
        var navigator = new IncomingOpeningsNavigatorViewModel(
            config, new RevitActionRunner(), fileInfo.Discipline, fileInfo, client);
        // Первая загрузка идёт прямо здесь: команда выполняется в контексте API, и внешнее событие
        // для неё не нужно. Обновление из панели пойдёт уже через событие.
        navigator.Reload(uiApplication);

        IncomingOpeningsPane.Attach(new IncomingOpeningsPaneViewModel(navigator));
        pane.Show();

        return Result.Succeeded;
    }
}
