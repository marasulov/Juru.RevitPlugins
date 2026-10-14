using System;
using System.Linq;

using Autodesk.Revit.DB;

namespace Juru.RevitPlugins.ProjectSetup.Services;

/// <summary>
/// Общие координаты файла дисциплины берутся с именованной площадки базового файла.
/// </summary>
/// <remarks>
/// <see cref="Document.AcquireCoordinates"/> здесь не годится: на связи, у которой больше одной
/// площадки, он падает с «Cannot acquire coordinates from a model with multiple named
/// locations», а у базовых файлов их всегда несколько — «По умолчанию», «Проект» и площадка
/// объекта после Publish. В интерфейсе Revit в этом случае спрашивает, какую взять; в API
/// такого выбора нет. Поэтому положение площадки переносится напрямую: проверено в Revit 2024
/// на поворот 30° и отметку 5.5 м, расхождение 0 мм (ClashTracker/docs/ProjectSetup.md, риск 1).
/// </remarks>
internal static class SiteCoordinates
{
    /// <summary>Положение внутреннего начала в общих координатах на площадке с этим именем.</summary>
    public static ProjectPosition Read(Document baseDocument, string siteName)
    {
        var site = new FilteredElementCollector(baseDocument).OfClass(typeof(ProjectLocation)).Cast<ProjectLocation>()
            .FirstOrDefault(l => string.Equals(l.Name, siteName, StringComparison.OrdinalIgnoreCase));

        if (site == null)
        {
            string existing = string.Join(", ", new FilteredElementCollector(baseDocument).OfClass(typeof(ProjectLocation))
                .Cast<ProjectLocation>().Select(l => $"«{l.Name}»"));
            throw new InvalidOperationException(
                $"В базовом файле нет площадки «{siteName}» — не выполнен Publish из генплана или площадка названа иначе. Есть: {existing}");
        }

        return site.GetProjectPosition(XYZ.Zero);
    }

    /// <summary>Ставит текущую площадку документа в то же место и даёт ей то же имя. Нужна открытая транзакция.</summary>
    public static void Apply(Document document, ProjectPosition position, string siteName)
    {
        var active = document.ActiveProjectLocation;
        active.SetProjectPosition(XYZ.Zero, position);

        // Площадка с таким именем уже есть в шаблоне — переименовывать текущую нельзя, имена уникальны
        bool nameTaken = new FilteredElementCollector(document).OfClass(typeof(ProjectLocation)).Cast<ProjectLocation>()
            .Any(l => l.Id != active.Id && string.Equals(l.Name, siteName, StringComparison.OrdinalIgnoreCase));
        if (!nameTaken)
            active.Name = siteName;
    }
}
