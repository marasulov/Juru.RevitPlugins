using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Juru.RevitPlugins.Core.FileNaming;
using Juru.RevitPlugins.Openings.Models.Configs;
using Juru.RevitPlugins.Openings.Models.Server;
using Juru.RevitPlugins.Openings.Models.Server.Dto;

namespace Juru.RevitPlugins.Openings.Commands;

/// <summary>
/// Отправляет все задания на отверстия, размещённые в активном файле, на сервер согласования -
/// см. <c>docs/OpeningsApi.md</c>.
/// </summary>
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

        if(!ProjectCoordinatesCheck.HasAcquiredCoordinates(doc)) {
            var confirm = TaskDialog.Show("Отправка заданий",
                "В файле не настроены общие координаты (Acquire Coordinates): "
                + "точка съёмки и базовая точка проекта совпадают с внутренними координатами.\n\n"
                + "Задания уйдут на сервер относительно базовой точки проекта - если общие координаты "
                + "не согласованы с файлом АР/КР, они встанут там со смещением.\n\n"
                + "Отправить всё равно?",
                TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No, TaskDialogResult.No);
            if(confirm != TaskDialogResult.Yes) {
                return Result.Cancelled;
            }
        }

        SaveOrSynchronize(doc);

        var batch = new OpeningBatchBuilder(doc, config).Build(fileInfo, "all", out var skipped);
        if(batch.Items.Count == 0) {
            TaskDialog.Show("Отправка заданий", "В файле нет заданий стандартного вида - отправлять нечего.");
            return Result.Cancelled;
        }

        try {
            var client = new OpeningsServerClient(serverUrl);
            var result = Task.Run(() => client.SubmitOpeningsAsync(batch)).GetAwaiter().GetResult();
            TaskDialog.Show("Отправка заданий", BuildReport(batch.Items.Count, skipped.Count, result));
        } catch(Exception exception) when(exception is InvalidOperationException or TimeoutException) {
            TaskDialog.Show("Отправка заданий", exception.Message);
            return Result.Failed;
        }

        return Result.Succeeded;
    }

    /// <summary>
    /// Сохраняет файл перед отправкой - из несохранённого плагин отправлять не даёт, иначе сервер
    /// знал бы задания, которых нет в файле, который увидит архитектор.
    /// </summary>
    private void SaveOrSynchronize(Document doc) {
        if(!doc.IsModified) {
            return;
        }

        if(doc.IsWorkshared) {
            doc.SynchronizeWithCentral(new TransactWithCentralOptions(), new SynchronizeWithCentralOptions());
        } else {
            doc.Save();
        }
    }

    private string BuildReport(int sent, int skippedCount, OpeningSubmitResultDto result) {
        var report = new StringBuilder();
        report.AppendLine($"Отправлено: {sent}");
        report.AppendLine($"Принято новых: {result.Accepted}, обновлено: {result.Updated}");
        if(skippedCount > 0) {
            report.AppendLine($"Пропущено нестандартных заданий (не описаны контрактом сервера): {skippedCount}");
        }

        if(result.Failed.Count > 0) {
            report.AppendLine();
            report.AppendLine($"Не отправлено: {result.Failed.Count}");
            foreach(var failure in result.Failed) {
                report.AppendLine($"  {failure.TaskKey}: {failure.Message}");
            }
        }

        return report.ToString();
    }
}
