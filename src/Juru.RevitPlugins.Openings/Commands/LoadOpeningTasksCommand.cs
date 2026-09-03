using System;
using System.Text;
using System.Threading.Tasks;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Juru.RevitPlugins.Core.FileNaming;
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

        if(!RevitFileNameInfo.TryParse(doc.PathName, out var fileInfo)) {
            TaskDialog.Show("Загрузка заданий",
                "Имя файла не соответствует стандарту наименования "
                + "(Проект-Объект-Дисциплина-vNN): без него нельзя определить проект и объект "
                + "для сервера.");
            return Result.Cancelled;
        }

        if(!fileInfo.Discipline.ReceivesOpeningTasks()) {
            TaskDialog.Show("Загрузка заданий",
                "Дисциплина файла - " + fileInfo.Discipline
                + ". Задания на отверстия принимаются в файлах ARC и STR.");
            return Result.Cancelled;
        }

        string serverUrl = OpeningsServerOptions.ResolveUrl(config);
        if(string.IsNullOrWhiteSpace(serverUrl)) {
            TaskDialog.Show("Загрузка заданий",
                "Адрес сервера не задан ни в настройках плагина, ни в ClashServer.json рядом со сборкой.");
            return Result.Cancelled;
        }

        try {
            var client = new OpeningsServerClient(serverUrl);
            var tasks = Task.Run(() => client.GetOpeningsAsync(fileInfo.ProjectCode, fileInfo.ObjectName))
                .GetAwaiter().GetResult();

            using var transactionGroup = new TransactionGroup(doc, "Загрузка заданий на отверстия");
            transactionGroup.Start();
            var result = new IncomingOpeningPlacer(doc, config).Place(tasks);
            transactionGroup.Assimilate();

            TaskDialog.Show("Загрузка заданий", BuildReport(tasks.Length, result));
        } catch(Exception exception) when(exception is InvalidOperationException or TimeoutException) {
            TaskDialog.Show("Загрузка заданий", exception.Message);
            return Result.Failed;
        }

        return Result.Succeeded;
    }

    private string BuildReport(int total, IncomingPlacementResult result) {
        var report = new StringBuilder();
        report.AppendLine($"Заданий на сервере: {total}");
        report.AppendLine($"Поставлено новых: {result.Added}");
        report.AppendLine($"Обновлено: {result.Updated}");
        if(result.Removed > 0) {
            report.AppendLine($"Убрано (отмечены удалёнными): {result.Removed}");
        }

        if(result.MissingFamilies.Count > 0) {
            report.AppendLine();
            report.AppendLine("Семейства не загружены в проект: " + string.Join(", ", result.MissingFamilies));
        }

        if(result.LoadedFamilies.Count > 0) {
            report.AppendLine();
            report.AppendLine("Загружены из папки эталонных семейств (в проекте их не было): "
                + string.Join(", ", result.LoadedFamilies));
        }

        if(result.UpdatedFamilies.Count > 0) {
            report.AppendLine();
            report.AppendLine("Обновлены эталоном (в проекте стояла не последняя версия): "
                + string.Join(", ", result.UpdatedFamilies));
        }

        if(result.MissingParameters.Count > 0) {
            report.AppendLine();
            report.AppendLine("Параметров нет в семействах, значения записать некуда:");
            report.AppendLine("  " + string.Join(", ", result.MissingParameters));
        }

        return report.ToString();
    }
}
