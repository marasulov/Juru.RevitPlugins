using System;
using System.Text;
using System.Threading.Tasks;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Juru.RevitPlugins.Core.FileNaming;
using Juru.RevitPlugins.Core.Localization;
using Juru.RevitPlugins.Openings.Models.Configs;
using Juru.RevitPlugins.Openings.Models.Server;

namespace Juru.RevitPlugins.Openings.Commands;

/// <summary>
/// Загружает задания на отверстия с сервера и ставит их в активном файле АР или КР -
/// см. "Как задания попадают в модель АР и КР" в <c>docs/OpeningsApi.md</c>.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class LoadOpeningTasksCommand : IExternalCommand {
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) {
        var doc = commandData.Application.ActiveUIDocument.Document;
        var config = OpeningsConfig.GetOpeningsConfig();
        var culture = PluginCulture.Resolve(config, commandData.Application.Application.Language);
        var localization = PluginLocalization.Load(typeof(LoadOpeningTasksCommand), culture);

        if(!RevitFileNameInfo.TryParse(doc.PathName, out var fileInfo)) {
            TaskDialog.Show(
                localization.GetString("Loading.DialogTitle"),
                localization.GetString("Navigator.WrongFileNameError"));
            return Result.Cancelled;
        }

        if(!fileInfo.Discipline.ReceivesOpeningTasks()) {
            TaskDialog.Show(
                localization.GetString("Loading.DialogTitle"),
                localization.GetString("Loading.WrongDisciplineErrorFormat", fileInfo.Discipline));
            return Result.Cancelled;
        }

        string serverUrl = OpeningsServerOptions.ResolveUrl(config);
        if(string.IsNullOrWhiteSpace(serverUrl)) {
            TaskDialog.Show(
                localization.GetString("Loading.DialogTitle"),
                localization.GetString("Approval.NoServerUrlError"));
            return Result.Cancelled;
        }

        try {
            var client = new OpeningsServerClient(serverUrl);
            var tasks = Task.Run(() => client.GetOpeningsAsync(fileInfo.ProjectCode, fileInfo.ObjectName))
                .GetAwaiter().GetResult();

            using var transactionGroup = new TransactionGroup(doc, localization.GetString("Loading.TransactionName"));
            transactionGroup.Start();
            var result = new IncomingOpeningPlacer(doc, config).Place(tasks);
            transactionGroup.Assimilate();

            // Прорезанные отверстия загрузка не трогает - у неё в руках только семейства заданий, -
            // поэтому про удалённые задания, по которым отверстие уже есть, она может лишь сказать.
            var cutDeleted = new RealOpeningReplacer(doc, config).FindCutDeleted(tasks);

            TaskDialog.Show(
                localization.GetString("Loading.DialogTitle"),
                BuildReport(tasks.Length, result, cutDeleted.Count, localization));
        } catch(Exception exception) when(exception is InvalidOperationException or TimeoutException) {
            TaskDialog.Show(localization.GetString("Loading.DialogTitle"), exception.Message);
            return Result.Failed;
        }

        return Result.Succeeded;
    }

    private string BuildReport(
        int total, IncomingPlacementResult result, int cutDeleted, PluginLocalization localization) {

        var report = new StringBuilder();
        report.AppendLine(localization.GetString("Loading.Report.TotalFormat", total));
        report.AppendLine(localization.GetString("Loading.Report.AddedFormat", result.Added));
        report.AppendLine(localization.GetString("Loading.Report.UpdatedFormat", result.Updated));
        if(result.Removed > 0) {
            report.AppendLine(localization.GetString("Loading.Report.RemovedFormat", result.Removed));
        }

        if(cutDeleted > 0) {
            report.AppendLine();
            report.AppendLine(localization.GetString("Loading.Report.CutDeletedFormat", cutDeleted));
        }

        if(result.MissingFamilies.Count > 0) {
            report.AppendLine();
            report.AppendLine(localization.GetString(
                "Report.MissingFamiliesFormat", string.Join(", ", result.MissingFamilies)));
        }

        if(result.LoadedFamilies.Count > 0) {
            report.AppendLine();
            report.AppendLine(localization.GetString(
                "Report.LoadedFamiliesFormat", string.Join(", ", result.LoadedFamilies)));
        }

        if(result.UpdatedFamilies.Count > 0) {
            report.AppendLine();
            report.AppendLine(localization.GetString(
                "Report.UpdatedFamiliesFormat", string.Join(", ", result.UpdatedFamilies)));
        }

        if(result.MissingParameters.Count > 0) {
            report.AppendLine();
            report.AppendLine(localization.GetString("Report.MissingParametersHeader"));
            report.AppendLine(localization.GetString(
                "Report.MissingParametersFormat", string.Join(", ", result.MissingParameters)));
        }

        return report.ToString();
    }
}
