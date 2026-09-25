using System;
using System.IO;
using System.Linq;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.ProjectSetup.Server;

namespace Juru.RevitPlugins.ProjectSetup.Services;

/// <summary>
/// Этап Б: файлы дисциплин из шаблонов. Координаты — с площадки базового файла, названной
/// кодом объекта (<see cref="SiteCoordinates"/>); связь с базовым файлом остаётся в файле.
/// </summary>
internal sealed class StageBExecutor(RevitFiles files)
{
    public JobOutcome Execute(StageBPayload plan)
    {
        var outcome = new JobOutcome();
        string Full(string relative) => JobPaths.Full(plan.UncRoot, relative);

        var existing = plan.Objects.SelectMany(o => o.Files).Where(f => File.Exists(Full(f.Path))).Select(f => f.Path).ToList();
        if (existing.Count > 0 && plan.OnExisting == ConflictModes.Ask)
        {
            outcome.Status = "needs_confirmation";
            outcome.Result.Conflicts.AddRange(existing);
            outcome.Log.Add("Уже существуют файлы дисциплин: " + string.Join(", ", existing));
            return outcome;
        }

        foreach (var item in plan.Objects)
        {
            var report = new ObjectResult { ObjectId = item.ObjectId };
            outcome.Result.Objects.Add(report);

            string basePath = Full(item.BasePath);
            ProjectPosition position;
            try
            {
                position = ReadSite(basePath, item.SiteName);
            }
            catch (Exception ex)
            {
                report.Error = ex.Message;
                outcome.Log.Add($"{item.BasePath}: ОШИБКА {ex.Message}");
                continue;
            }

            foreach (var file in item.Files)
            {
                string path = Full(file.Path);
                if (File.Exists(path))
                {
                    // Пропуск — решение человека на странице: файл уже есть, значит дисциплина готова
                    report.Created.Add(file.Discipline);
                    outcome.Log.Add($"{file.Path}: уже есть, оставлен как есть");
                    continue;
                }

                Document? document = null;
                try
                {
                    document = files.NewFromTemplate(file.Template, path);
                    CreateDisciplineFile(document, path, basePath, position, item.SiteName, plan.AsCentral);
                    document.Close(false);
                    document = null;
                    report.Created.Add(file.Discipline);
                    outcome.Log.Add($"{file.Path}: создан, площадка «{item.SiteName}»");
                }
                catch (Exception ex)
                {
                    RevitFiles.DiscardNew(document, path);
                    report.Error = $"{file.Discipline}: {ex.Message}";
                    outcome.Log.Add($"{file.Path}: ОШИБКА {ex.Message}");
                }
            }

            // Объект готов, только если созданы все дисциплины этапа
            report.Ok = report.Error == null;
        }

        if (outcome.Result.Objects.All(o => !o.Ok))
            outcome.Status = "failed";
        return outcome;
    }

    private void CreateDisciplineFile(
        Document document, string path, string basePath, ProjectPosition position, string siteName, bool asCentral)
    {
        using (var transaction = new Transaction(document, "Координаты с базового файла"))
        {
            transaction.Start();
            SiteCoordinates.Apply(document, position, siteName);
            transaction.Commit();
        }

        if (asCentral)
        {
            // Центральную модель после сохранения не пересохранить обычным Save, поэтому связь
            // подгружается до него — с полным путём, относительный считать ещё не от чего
            AddBaseLink(document, basePath);
            RevitFiles.SaveNew(document, path, asCentral: true);
            return;
        }

        // Обычный файл сначала сохраняется: тогда связь получит относительный путь
        RevitFiles.SaveNew(document, path);
        AddBaseLink(document, basePath);
        document.Save();
        RevitFiles.DeleteFirstBackup(path);
    }

    private void AddBaseLink(Document document, string basePath)
    {
        using var transaction = new Transaction(document, "Связь с базовым файлом");
        transaction.Start();
        files.AddLink(document, basePath);
        transaction.Commit();
    }

    /// <summary>
    /// Площадка читается из уже загруженного документа, если он есть в сессии (например, связью
    /// в открытом генплане), иначе базовый файл открывается и сразу закрывается: открытый
    /// документ нельзя подгрузить связью.
    /// </summary>
    private ProjectPosition ReadSite(string basePath, string siteName)
    {
        if (!File.Exists(basePath))
            throw new FileNotFoundException($"Нет базового файла {basePath} — сначала этап А");

        var open = files.FindOpen(basePath);
        if (open is { IsLinked: false })
            throw new InvalidOperationException($"{Path.GetFileName(basePath)} открыт как отдельный документ — закройте его");
        if (open != null)
            return SiteCoordinates.Read(open, siteName);

        var modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(basePath);
        var document = files.Application.OpenDocumentFile(modelPath, new OpenOptions());
        try
        {
            return SiteCoordinates.Read(document, siteName);
        }
        finally
        {
            document.Close(false);
        }
    }
}
