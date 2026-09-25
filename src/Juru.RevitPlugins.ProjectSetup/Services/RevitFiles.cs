using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Autodesk.Revit.DB;

using Application = Autodesk.Revit.ApplicationServices.Application;

namespace Juru.RevitPlugins.ProjectSetup.Services;

/// <summary>Операции с файлами проекта, общие для этапов А и Б.</summary>
internal sealed class RevitFiles(Application application)
{
    public Application Application => application;

    /// <summary>Документ этого файла, если он уже открыт в сессии — главным окном или связью.</summary>
    public Document? FindOpen(string path) =>
        application.Documents.Cast<Document>()
            .FirstOrDefault(d => string.Equals(d.PathName, path, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Новый несохранённый документ из шаблона. Папку назначения создаёт агент ФС вместе
    /// с правами, поэтому её отсутствие — ошибка, а не повод создать папку без прав.
    /// </summary>
    public Document NewFromTemplate(string template, string targetPath)
    {
        if (!File.Exists(template))
            throw new FileNotFoundException($"Шаблон не найден: {template}");

        string directory = Path.GetDirectoryName(targetPath) ?? "";
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException(
                $"Нет папки {directory}: её создаёт агент ФС на сервере, задание папок должно быть выполнено раньше");

        return application.NewProjectDocument(template);
    }

    /// <summary>
    /// Шаблоны, которых нет на диске, среди нужных заданию. Проверяются все до первого файла:
    /// шаблоны лежат на общем диске и переезжают при переименовании папок, и узнать об этом на
    /// середине очереди — значит получить проект, собранный наполовину.
    /// </summary>
    public static List<string> MissingTemplates(IEnumerable<string> templates) =>
        templates.Distinct(StringComparer.OrdinalIgnoreCase).Where(t => !File.Exists(t)).ToList();

    /// <summary>Первое сохранение нового файла. Существующий файл не перезаписывается никогда.</summary>
    public static void SaveNew(Document document, string path, bool asCentral = false)
    {
        var options = new SaveAsOptions { OverwriteExistingFile = false };
        if (asCentral)
        {
            // Рабочие наборы по умолчанию; наборы по стандарту — отдельная задача
            if (!document.IsWorkshared)
                document.EnableWorksharing("Shared Levels and Grids", "Workset1");
            options.SetWorksharingOptions(new WorksharingSaveAsOptions { SaveAsCentral = true });
        }

        document.SaveAs(path, options);
    }

    /// <summary>
    /// Связь «начало к началу». Путь относительный, если хост уже сохранён: такой путь переживает
    /// переименование сервера и копирование проекта, UNC — нет. У несохранённого хоста считать
    /// относительный путь не от чего, и связь получает полный путь.
    /// </summary>
    public RevitLinkInstance AddLink(Document host, string linkPath)
    {
        if (FindOpen(linkPath) is { IsLinked: false })
            throw new InvalidOperationException(
                $"{Path.GetFileName(linkPath)} открыт как отдельный документ — закройте его: открытый файл нельзя подгрузить связью");

        var modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(linkPath);
        bool relative = !string.IsNullOrEmpty(host.PathName);
        var load = RevitLinkType.Create(host, modelPath, new RevitLinkOptions(relative));
        if (load.LoadResult != LinkLoadResultType.LinkLoaded)
            throw new InvalidOperationException($"Связь {Path.GetFileName(linkPath)} не загрузилась: {load.LoadResult}");

        return RevitLinkInstance.Create(host, load.ElementId, ImportPlacement.Origin);
    }

    /// <summary>Есть ли в документе связь с этим файлом (сравнение по полному пути).</summary>
    public static bool HasLinkTo(Document host, string linkPath) =>
        new FilteredElementCollector(host).OfClass(typeof(RevitLinkType)).Cast<RevitLinkType>()
            .Select(t => t.GetExternalFileReference()?.GetAbsolutePath())
            .Where(p => p != null)
            .Any(p => string.Equals(ModelPathUtils.ConvertModelPathToUserVisiblePath(p), linkPath, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Revit при повторном сохранении кладёт рядом копию <c>имя.0001.rvt</c>. У файла, созданного
    /// в этом же прогоне, это копия пустого шаблона без связи — она только путает. Копии файлов,
    /// существовавших до прогона, не трогаются: это обычная защита Revit от ошибок пользователя.
    /// </summary>
    public static void DeleteFirstBackup(string path)
    {
        string backup = Path.Combine(Path.GetDirectoryName(path) ?? "", Path.GetFileNameWithoutExtension(path) + ".0001.rvt");
        try { if (File.Exists(backup)) File.Delete(backup); } catch { /* лишняя копия — не повод ронять задание */ }
    }

    /// <summary>Файл, созданный в этом прогоне и не доделанный, удаляется: иначе повтор счёл бы его готовым.</summary>
    public static void DiscardNew(Document? document, string path)
    {
        try { document?.Close(false); } catch { /* документ мог уже закрыться */ }
        try { if (File.Exists(path)) File.Delete(path); } catch { /* останется конфликтом при повторе — это видно */ }
    }
}
