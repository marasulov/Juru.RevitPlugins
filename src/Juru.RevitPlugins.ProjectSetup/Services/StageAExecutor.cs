using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.ProjectSetup.Server;

namespace Juru.RevitPlugins.ProjectSetup.Services;

/// <summary>
/// Этап А: базовые файлы объектов из шаблона и генплан, в который они подгружены связями
/// «начало к началу». Расставить их по контурам и сделать Publish — ручная работа BIM-менеджера.
/// </summary>
internal sealed class StageAExecutor(RevitFiles files)
{
    public JobOutcome Execute(StageAPayload plan, IJobProgress progress)
    {
        var outcome = new JobOutcome();
        string Full(string relative) => JobPaths.Full(plan.UncRoot, relative);

        // Конфликты — до первого изменения: либо задание целиком ждёт решения человека,
        // либо выполняется, а не останавливается посередине
        var existing = plan.Bases.Where(b => File.Exists(Full(b.Path))).Select(b => b.Path).ToList();
        if (existing.Count > 0 && plan.OnExisting == ConflictModes.Ask)
        {
            outcome.Status = "needs_confirmation";
            outcome.Result.Conflicts.AddRange(existing);
            outcome.Log.Add("Уже существуют базовые файлы: " + string.Join(", ", existing));
            return outcome;
        }

        var ready = new List<BaseFileDto>();
        int total = plan.Bases.Count + 1;
        for (int i = 0; i < plan.Bases.Count; i++)
        {
            var baseFile = plan.Bases[i];
            string path = Full(baseFile.Path);
            progress.Step(i, total, "базовый файл " + Path.GetFileName(baseFile.Path));
            var report = new ObjectResult { ObjectId = baseFile.ObjectId };
            outcome.Result.Objects.Add(report);

            if (File.Exists(path))
            {
                outcome.Log.Add($"{baseFile.Path}: уже есть, оставлен как есть");
                ready.Add(baseFile);
                continue;
            }

            Document? document = null;
            try
            {
                document = files.NewFromTemplate(baseFile.Template, path);
                RevitFiles.SaveNew(document, path);
                document.Close(false);
                document = null;
                outcome.Log.Add($"{baseFile.Path}: создан");
                ready.Add(baseFile);
            }
            catch (Exception ex)
            {
                RevitFiles.DiscardNew(document, path);
                report.Error = ex.Message;
                outcome.Log.Add($"{baseFile.Path}: ОШИБКА {ex.Message}");
            }
        }

        // Генплан. Существующий — не конфликт: объект добавили в готовый проект, и в генплан
        // нужно только подгрузить новую связь
        string? genplanError = null;
        progress.Step(total - 1, total, "генплан " + Path.GetFileName(plan.Genplan.Path) + ": связи базовых файлов");
        try
        {
            LinkIntoGenplan(plan, ready.Select(b => Full(b.Path)).ToList(), outcome);
        }
        catch (Exception ex)
        {
            genplanError = "генплан: " + ex.Message;
            outcome.Log.Add("ОШИБКА " + genplanError);
        }

        foreach (var report in outcome.Result.Objects)
        {
            bool baseReady = ready.Any(b => b.ObjectId == report.ObjectId);
            report.Ok = baseReady && genplanError == null;
            if (baseReady && genplanError != null)
                report.Error = genplanError;
        }

        if (outcome.Result.Objects.All(o => !o.Ok))
            outcome.Status = "failed";
        return outcome;
    }

    private void LinkIntoGenplan(StageAPayload plan, List<string> basePaths, JobOutcome outcome)
    {
        string path = JobPaths.Full(plan.UncRoot, plan.Genplan.Path);

        // Генплан может быть открыт у BIM-менеджера — тогда работаем в нём, но не закрываем
        var document = files.FindOpen(path);
        bool openedHere = document == null;
        bool created = false;

        if (document == null && File.Exists(path))
        {
            document = OpenForEdit(path);
        }
        else if (document == null)
        {
            document = files.NewFromTemplate(plan.Genplan.Template, path);
            RevitFiles.SaveNew(document, path);
            created = true;
            outcome.Log.Add($"{plan.Genplan.Path}: создан");
        }
        else if (document.IsLinked)
        {
            throw new InvalidOperationException("генплан загружен в сессии связью другого файла — закройте этот файл");
        }

        try
        {
            var missing = basePaths.Where(p => !RevitFiles.HasLinkTo(document, p)).ToList();
            if (missing.Count > 0)
            {
                using (var transaction = new Transaction(document, "Связи базовых файлов"))
                {
                    transaction.Start();
                    foreach (string basePath in missing)
                        files.AddLink(document, basePath);
                    transaction.Commit();
                }

                document.Save();
                if (created) RevitFiles.DeleteFirstBackup(path);
                outcome.Log.Add($"{plan.Genplan.Path}: подгружено связей {missing.Count}" +
                                (openedHere ? "" : " (файл был открыт — сохранён вместе с вашими правками)"));
            }
        }
        catch
        {
            if (created) { RevitFiles.DiscardNew(document, path); document = null; }
            throw;
        }
        finally
        {
            if (openedHere) document?.Close(false);
        }
    }

    private Document OpenForEdit(string path)
    {
        var modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(path);
        return files.Application.OpenDocumentFile(modelPath, new OpenOptions());
    }
}

internal static class JobPaths
{
    /// <summary>Путь из задания (относительный, через «\») — под корнем проектов, как его видит эта машина.</summary>
    public static string Full(string root, string relative) =>
        Path.Combine(new[] { root.TrimEnd('\\', '/') }.Concat(relative.Split('\\', '/')).ToArray());
}
