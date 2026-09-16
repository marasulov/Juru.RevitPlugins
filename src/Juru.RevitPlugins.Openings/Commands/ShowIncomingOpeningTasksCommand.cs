using System;
using System.Windows.Interop;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Juru.RevitPlugins.Core.FileNaming;
using Juru.RevitPlugins.Core.Localization;
using Juru.RevitPlugins.Core.Theming;
using Juru.RevitPlugins.Openings.Models.Configs;
using Juru.RevitPlugins.Openings.Models.Server;
using Juru.RevitPlugins.Openings.Services;
using Juru.RevitPlugins.Openings.ViewModels.Navigator;
using Juru.RevitPlugins.Openings.Views.Navigator;

namespace Juru.RevitPlugins.Openings.Commands;

/// <summary>
/// Открывает навигатор согласования: задания на отверстия, полученные с сервера, в активном файле
/// АР или КР.
/// </summary>
/// <remarks>
/// Окно немодальное по той же причине, что и у исходящего навигатора: решения копятся, пока смотришь
/// задания в модели, и держать Revit заблокированным нельзя.
/// </remarks>
[Transaction(TransactionMode.Manual)]
public class ShowIncomingOpeningTasksCommand : IExternalCommand {
    private static IncomingOpeningsNavigatorWindow _window;

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) {
        // Необработанное исключение здесь Revit показывает как безымянную внутреннюю ошибку,
        // по которой причину не найти, - поэтому показываем своё окно с типом и стеком.
        try {
            return ShowNavigator(commandData);
        } catch(Exception exception) {
            return CommandErrorReport.Show("Согласование заданий", exception);
        }
    }

    private Result ShowNavigator(ExternalCommandData commandData) {
        var uiApplication = commandData.Application;
        var doc = uiApplication.ActiveUIDocument.Document;
        var config = OpeningsConfig.GetOpeningsConfig();
        var culture = PluginCulture.Resolve(config, uiApplication.Application.Language);
        var localization = PluginLocalization.Load(typeof(ShowIncomingOpeningTasksCommand), culture);

        if(!RevitFileNameInfo.TryParse(doc.PathName, out var fileInfo)) {
            TaskDialog.Show(
                localization.GetString("Approval.DialogTitle"),
                localization.GetString("Approval.WrongFileNameError"));
            return Result.Cancelled;
        }

        if(!fileInfo.Discipline.ReceivesOpeningTasks()) {
            TaskDialog.Show(
                localization.GetString("Approval.DialogTitle"),
                localization.GetString("Approval.WrongDisciplineErrorFormat", fileInfo.Discipline));
            return Result.Cancelled;
        }

        string serverUrl = OpeningsServerOptions.ResolveUrl(config);
        if(string.IsNullOrWhiteSpace(serverUrl)) {
            TaskDialog.Show(
                localization.GetString("Approval.DialogTitle"),
                localization.GetString("Approval.NoServerUrlError"));
            return Result.Cancelled;
        }

        if(_window is not null) {
            _window.Activate();
            return Result.Succeeded;
        }

        JuruTheme.Apply();

        var client = new OpeningsServerClient(serverUrl);
        var viewModel = new IncomingOpeningsNavigatorViewModel(
            config, localization, new RevitActionRunner(), fileInfo.Discipline, fileInfo, client);
        // Первая загрузка идёт прямо здесь: команда выполняется в контексте API, и внешнее событие
        // для неё не нужно. Обновление из окна пойдёт уже через событие.
        viewModel.Reload(uiApplication);

        _window = new IncomingOpeningsNavigatorWindow(localization) { DataContext = viewModel };
        _window.Closed += (_, _) => _window = null;
        new WindowInteropHelper(_window) { Owner = uiApplication.MainWindowHandle };
        _window.Show();

        return Result.Succeeded;
    }
}
