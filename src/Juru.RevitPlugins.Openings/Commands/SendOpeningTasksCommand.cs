using System;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Juru.RevitPlugins.Core.FileNaming;
using Juru.RevitPlugins.Openings.Models.Configs;
using Juru.RevitPlugins.Openings.Models.Server;

namespace Juru.RevitPlugins.Openings.Commands;

/// <summary>
/// Отправляет все задания на отверстия, размещённые в активном файле, на сервер согласования -
/// см. <c>docs/OpeningsApi.md</c>.
/// </summary>
/// <remarks>
/// Сама отправка живёт в <see cref="OpeningTasksSender"/>: то же действие есть в навигаторе
/// по выданным заданиям, где инженер рядом отмечает согласующие дисциплины.
/// </remarks>
[Transaction(TransactionMode.Manual)]
public class SendOpeningTasksCommand : IExternalCommand {
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) {
        var doc = commandData.Application.ActiveUIDocument.Document;
        var config = OpeningsConfig.GetOpeningsConfig();

        if(!RevitFileNameInfo.TryParse(doc.PathName, out var fileInfo)) {
            TaskDialog.Show("Отправка заданий",
                "Имя файла не соответствует стандарту наименования "
                + "(Проект-Объект-Дисциплина-vNN): без него нельзя определить проект и объект "
                + "для сервера.");
            return Result.Cancelled;
        }

        if(!fileInfo.Discipline.IssuesOpeningTasks()) {
            TaskDialog.Show("Отправка заданий",
                "Дисциплина файла - " + fileInfo.Discipline
                + ". Задания на отверстия отправляются из файлов WSS, ELE и HVC.");
            return Result.Cancelled;
        }

        string serverUrl = OpeningsServerOptions.ResolveUrl(config);
        if(string.IsNullOrWhiteSpace(serverUrl)) {
            TaskDialog.Show("Отправка заданий",
                "Адрес сервера не задан ни в настройках плагина, ни в ClashServer.json рядом со сборкой.");
            return Result.Cancelled;
        }

        var sender = new OpeningTasksSender(doc, config, new OpeningsServerClient(serverUrl));
        if(sender.NeedsCoordinateWarning && !ConfirmWithoutSharedCoordinates()) {
            return Result.Cancelled;
        }

        try {
            var outcome = sender.Send(fileInfo);
            TaskDialog.Show("Отправка заданий", outcome.Report);
            return outcome.IsRefused ? Result.Cancelled : Result.Succeeded;
        } catch(Exception exception) when(exception is InvalidOperationException or TimeoutException) {
            TaskDialog.Show("Отправка заданий", exception.Message);
            return Result.Failed;
        }
    }

    private bool ConfirmWithoutSharedCoordinates() {
        var confirm = TaskDialog.Show("Отправка заданий",
            "В файле не настроены общие координаты (Acquire Coordinates): "
            + "точка съёмки и базовая точка проекта совпадают с внутренними координатами.\n\n"
            + "Задания уйдут на сервер относительно базовой точки проекта - если общие координаты "
            + "не согласованы с файлом АР/КР, они встанут там со смещением.\n\n"
            + "Отправить всё равно?",
            TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No, TaskDialogResult.No);
        return confirm == TaskDialogResult.Yes;
    }
}
