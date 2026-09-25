using System;
using System.Diagnostics;
using System.IO;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

using Juru.RevitPlugins.ProjectSetup.Server;
using Juru.RevitPlugins.ProjectSetup.Services;

using Nice3point.Revit.Toolkit.External;

namespace Juru.RevitPlugins.ProjectSetup.Commands;

/// <summary>
///     Выполняет Revit-задания подготовки проекта из очереди ClashTracker: генплан и базовые
///     файлы (этап А), файлы дисциплин с координатами площадки объекта (этап Б).
///     Кнопки на ленте нет — запускается через Add-in Manager.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public sealed class RunProjectSetupJobsCommand : ExternalCommand
{
    public override void Execute()
    {
        string url = SetupJobsClient.ReadDefaultUrl();
        string worker = $"{Environment.MachineName}\\{Environment.UserName} (Revit {Application.Application.VersionNumber})";

        using var client = new SetupJobsClient(url, worker);
        var log = new SetupJobRunner(client, new RevitFiles(Application.Application)).RunAll();

        string logPath = WriteLog(log);
        var dialog = new TaskDialog("Подготовка проекта")
        {
            MainInstruction = "Задания ClashTracker обработаны",
            MainContent = string.Join(Environment.NewLine, log.Count > 25 ? log.GetRange(log.Count - 25, 25) : log)
                          + $"{Environment.NewLine}{Environment.NewLine}Сервер: {client.BaseUrl}",
            CommonButtons = TaskDialogCommonButtons.Close
        };
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Открыть журнал");

        if (dialog.Show() is TaskDialogResult.CommandLink1)
            Process.Start("notepad.exe", $"\"{logPath}\"");
    }

    private static string WriteLog(System.Collections.Generic.List<string> lines)
    {
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Juru", "ProjectSetup");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"project-setup-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        File.WriteAllLines(path, lines);
        return path;
    }
}
