using System.Windows.Interop;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Juru.RevitPlugins.Core.FileNaming;
using Juru.RevitPlugins.Core.Theming;
using Juru.RevitPlugins.Openings.Models.Configs;
using Juru.RevitPlugins.Openings.Services;
using Juru.RevitPlugins.Openings.ViewModels.Navigator;
using Juru.RevitPlugins.Openings.Views.Navigator;

namespace Juru.RevitPlugins.Openings.Commands;

/// <summary>
/// Открывает навигатор по заданиям на отверстия, выданным из активного файла.
/// </summary>
/// <remarks>
/// Окно немодальное: по списку заданий ходят, попутно правя модель, поэтому держать Revit
/// заблокированным нельзя. Экземпляр один на сеанс - второй показывал бы состояния, посчитанные
/// в другой момент, и было бы неясно, какому из них верить.
/// </remarks>
[Transaction(TransactionMode.Manual)]
public class ShowOpeningTasksCommand : IExternalCommand {
    private static OpeningsNavigatorWindow _window;

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) {
        var uiApplication = commandData.Application;
        var doc = uiApplication.ActiveUIDocument.Document;
        var config = OpeningsConfig.GetOpeningsConfig();

        if(!IsMepDocument(doc, config, out string error)) {
            TaskDialog.Show("Навигатор по заданиям", error);
            return Result.Cancelled;
        }

        if(_window is not null) {
            _window.Activate();
            return Result.Succeeded;
        }

        JuruTheme.Apply();

        var viewModel = new OpeningsNavigatorViewModel(config, new RevitActionRunner());
        // Первая загрузка идёт прямо здесь: команда выполняется в контексте API, и внешнее событие
        // для неё не нужно. Обновление из окна пойдёт уже через событие.
        viewModel.Reload(uiApplication);

        _window = new OpeningsNavigatorWindow { DataContext = viewModel };
        _window.Closed += (_, _) => _window = null;
        new WindowInteropHelper(_window) { Owner = uiApplication.MainWindowHandle };
        _window.Show();

        return Result.Succeeded;
    }

    /// <summary>
    /// Проверяет, что файл выдаёт задания на отверстия: пока навигатор показывает только исходящие.
    /// </summary>
    private bool IsMepDocument(Document doc, OpeningsConfig config, out string error) {
        error = null;
        if(!new DisciplineResolver(config).TryResolve(doc.PathName, out var discipline, out _)) {
            error = "Дисциплину файла определить не удалось: имя не соответствует стандарту "
                + "(Проект-Объект-Дисциплина-vNN) и не подходит ни под одно правило из настроек.";
            return false;
        }

        if(!discipline.IssuesOpeningTasks()) {
            error = "Дисциплина файла - " + discipline
                + ". Навигатор показывает исходящие задания и работает в файлах WSS, ELE и HVC. "
                + "Просмотр входящих заданий в файлах ARC и STR появится вместе с их согласованием.";
            return false;
        }

        return true;
    }
}
