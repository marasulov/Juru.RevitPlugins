using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Autodesk.Revit.UI;

using Juru.RevitPlugins.ProjectSetup.Server;

using Nice3point.Revit.Toolkit;

namespace Juru.RevitPlugins.ProjectSetup.Services;

/// <summary>
/// Берёт Revit-задания из очереди ClashTracker по одному и выполняет, пока очередь не опустеет.
/// Работает в контексте Revit API: HTTP-вызовы — синхронно, через Task.Run, как в «Отверстиях».
/// </summary>
internal sealed class SetupJobRunner(SetupJobsClient client, RevitFiles files)
{
    // Без предела одна ошибка отчёта (сервер не принял) крутила бы одно и то же задание вечно
    private const int MaxJobsPerRun = 50;

    public List<string> RunAll()
    {
        var log = new List<string>();
        var window = new ProgressWindow();
        try
        {
            RunAll(log, window);
        }
        finally
        {
            window.Close();
        }

        return log;
    }

    private void RunAll(List<string> log, ProgressWindow window)
    {
        window.Show("Подготовка проекта", "Проверка очереди заданий…", 0, 1);

        for (int i = 0; i < MaxJobsPerRun; i++)
        {
            var job = Task.Run(client.ClaimAsync).GetAwaiter().GetResult();
            if (job == null)
            {
                log.Add(i == 0 ? "Очередь пуста: заданий для Revit нет." : "Очередь пуста.");
                break;
            }

            log.Add($"Задание #{job.Id} ({job.Kind})");
            string title = job.Kind == JobKinds.StageB
                ? $"Задание #{job.Id}: файлы дисциплин"
                : $"Задание #{job.Id}: генплан и базовые файлы";
            var outcome = Execute(job, new JobProgress(window, client, job, title));
            foreach (string line in outcome.Log) log.Add("  " + line);

            Task.Run(() => client.CompleteAsync(job, outcome)).GetAwaiter().GetResult();
            log.Add($"Задание #{job.Id}: {Describe(outcome.Status)}");
        }
    }

    private JobOutcome Execute(ClaimedJob job, IJobProgress progress)
    {
        try
        {
            // Диалоги (предупреждения при открытии, сохранении, загрузке связей) закрываются сами:
            // пакетная работа не должна встать на модальном окне посреди очереди
            using (RevitContext.BeginDialogSuppressionScope(TaskDialogResult.Ok))
            {
                return job.Kind switch
                {
                    JobKinds.StageA => new StageAExecutor(files).Execute(SetupJobsClient.ReadPayload<StageAPayload>(job), progress),
                    JobKinds.StageB => new StageBExecutor(files).Execute(SetupJobsClient.ReadPayload<StageBPayload>(job), progress),
                    _ => Failed($"Неизвестный вид задания {job.Kind}")
                };
            }
        }
        catch (Exception ex)
        {
            // Задание уже взято: без отчёта оно осталось бы running, пока его не повторят руками
            return Failed(ex.ToString());
        }
    }

    private static JobOutcome Failed(string message)
    {
        var outcome = new JobOutcome { Status = "failed" };
        outcome.Log.Add("ОШИБКА: " + message);
        return outcome;
    }

    private static string Describe(string status) => status switch
    {
        "done" => "выполнено",
        "needs_confirmation" => "файлы уже есть — решите на странице проекта (пропустить или отменить)",
        _ => "ошибка"
    };
}
