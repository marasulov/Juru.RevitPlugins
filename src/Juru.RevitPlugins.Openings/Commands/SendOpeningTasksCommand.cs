using System;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Juru.RevitPlugins.Core.FileNaming;
using Juru.RevitPlugins.Core.Localization;
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
        var culture = PluginCulture.Resolve(config, commandData.Application.Application.Language);
        var localization = PluginLocalization.Load(typeof(SendOpeningTasksCommand), culture);

        if(!RevitFileNameInfo.TryParse(doc.PathName, out var fileInfo)) {
            TaskDialog.Show(
                localization.GetString("Send.DialogTitle"),
                localization.GetString("Navigator.WrongFileNameError"));
            return Result.Cancelled;
        }

        if(!fileInfo.Discipline.IssuesOpeningTasks()) {
            TaskDialog.Show(
                localization.GetString("Send.DialogTitle"),
                localization.GetString("Send.WrongDisciplineErrorFormat", fileInfo.Discipline));
            return Result.Cancelled;
        }

        string serverUrl = OpeningsServerOptions.ResolveUrl(config);
        if(string.IsNullOrWhiteSpace(serverUrl)) {
            TaskDialog.Show(
                localization.GetString("Send.DialogTitle"),
                localization.GetString("Approval.NoServerUrlError"));
            return Result.Cancelled;
        }

        var sender = new OpeningTasksSender(doc, config, new OpeningsServerClient(serverUrl));
        if(sender.NeedsCoordinateWarning && !ConfirmWithoutSharedCoordinates(localization)) {
            return Result.Cancelled;
        }

        try {
            var outcome = sender.Send(fileInfo);
            TaskDialog.Show(localization.GetString("Send.DialogTitle"), outcome.Report);
            return outcome.IsRefused ? Result.Cancelled : Result.Succeeded;
        } catch(Exception exception) when(exception is InvalidOperationException or TimeoutException) {
            TaskDialog.Show(localization.GetString("Send.DialogTitle"), exception.Message);
            return Result.Failed;
        }
    }

    private bool ConfirmWithoutSharedCoordinates(PluginLocalization localization) {
        var confirm = TaskDialog.Show(
            localization.GetString("Send.DialogTitle"),
            localization.GetString("Navigator.NoSharedCoordinatesText"),
            TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No, TaskDialogResult.No);
        return confirm == TaskDialogResult.Yes;
    }
}
