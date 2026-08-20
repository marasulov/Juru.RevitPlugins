using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Core.FileNaming;
using Juru.RevitPlugins.Openings.Models.Configs;

namespace Juru.RevitPlugins.Openings.Models.Intersections;

/// <summary>
/// Находит в активном файле связи с конструкциями, в которых нужно искать пересечения.
/// </summary>
/// <remarks>
/// Связи отбираются по дисциплине из имени файла, а не по первой попавшейся загруженной связи:
/// в файле инженера обычно подгружены и архитектура, и конструкции, и смежные инженерные разделы.
/// </remarks>
public class StructureLinksProvider {
    private readonly Document _doc;
    private readonly OpeningsConfig _config;
    private readonly DisciplineResolver _disciplineResolver;

    public StructureLinksProvider(Document doc, OpeningsConfig config) {
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _disciplineResolver = new DisciplineResolver(config);
    }

    /// <summary>
    /// Возвращает провайдеры конструкций по всем загруженным связям архитектуры и конструкций.
    /// </summary>
    public IReadOnlyCollection<StructureElementsProvider> GetProviders() {
        var kinds = GetStructureKinds();
        if(kinds.Count == 0) {
            return [];
        }

        List<StructureElementsProvider> providers = [];
        foreach(var linkInstance in GetLinkInstances()) {
            if(!TryGetStructureDiscipline(linkInstance, out var discipline)) {
                continue;
            }

            providers.Add(new StructureElementsProvider(linkInstance, discipline, kinds));
        }

        return providers;
    }

    /// <summary>
    /// Возвращает связи, имя файла которых не удалось разобрать по стандарту наименования.
    /// Такие связи в поиске не участвуют, и об этом нужно сказать пользователю.
    /// </summary>
    public IReadOnlyCollection<string> GetUnrecognizedLinkNames() {
        return GetLinkInstances()
            .Where(link => !_disciplineResolver.TryResolve(GetLinkFileName(link), out _, out _))
            .Select(link => link.Name)
            .ToArray();
    }

    /// <summary>
    /// Возвращает виды конструкций, которые нужно проверять хотя бы для одного выбранного вида элементов.
    /// </summary>
    private IReadOnlyCollection<StructureKind> GetStructureKinds() {
        return _config.GetSelectedCategories()
            .SelectMany(settings => settings.Structures)
            .Distinct()
            .ToArray();
    }

    private IReadOnlyCollection<RevitLinkInstance> GetLinkInstances() {
        return new FilteredElementCollector(_doc)
            .OfClass(typeof(RevitLinkInstance))
            .Cast<RevitLinkInstance>()
            .Where(link => link.GetLinkDocument() is not null)
            .ToArray();
    }

    private bool TryGetStructureDiscipline(RevitLinkInstance linkInstance, out Discipline discipline) {
        if(!_disciplineResolver.TryResolve(GetLinkFileName(linkInstance), out discipline, out _)) {
            return false;
        }

        return discipline.ReceivesOpeningTasks();
    }

    private static string GetLinkFileName(RevitLinkInstance linkInstance) {
        return linkInstance.GetLinkDocument()?.PathName ?? linkInstance.Name;
    }
}
