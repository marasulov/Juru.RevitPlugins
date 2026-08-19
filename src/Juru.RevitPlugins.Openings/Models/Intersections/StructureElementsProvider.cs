using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Openings.Models.Configs;
using Juru.RevitPlugins.Core.FileNaming;

namespace Juru.RevitPlugins.Openings.Models.Intersections;

/// <summary>
/// Отбирает конструкции из связанного файла архитектуры или конструкций.
/// </summary>
public class StructureElementsProvider : IElementsProvider {
    private readonly IReadOnlyCollection<StructureKind> _kinds;
    private List<Element> _elements;

    /// <param name="linkInstance">Экземпляр связи с конструкциями.</param>
    /// <param name="discipline">Дисциплина связанного файла.</param>
    /// <param name="kinds">Виды конструкций, которые нужно проверять.</param>
    public StructureElementsProvider(
        RevitLinkInstance linkInstance,
        Discipline discipline,
        IReadOnlyCollection<StructureKind> kinds) {

        if(linkInstance is null) { throw new ArgumentNullException(nameof(linkInstance)); }
        Doc = linkInstance.GetLinkDocument() ?? throw new ArgumentException(
            "Связь не загружена, документа у неё нет.", nameof(linkInstance));
        MainTransform = linkInstance.GetTotalTransform();
        Discipline = discipline;
        LinkName = linkInstance.Name;
        _kinds = kinds ?? throw new ArgumentNullException(nameof(kinds));
    }

    public Document Doc { get; }

    public Transform MainTransform { get; }

    /// <summary>
    /// Дисциплина связанного файла.
    /// </summary>
    public Discipline Discipline { get; }

    /// <summary>
    /// Имя экземпляра связи, для сообщений пользователю.
    /// </summary>
    public string LinkName { get; }

    public IReadOnlyCollection<Element> GetElements() {
        return _elements ??= Collect();
    }

    /// <summary>
    /// Возвращает вид конструкции для элемента этого провайдера.
    /// </summary>
    public static StructureKind GetKind(Element element) {
        return element is Wall ? StructureKind.Wall : StructureKind.Floor;
    }

    private List<Element> Collect() {
        var categories = _kinds.Select(kind => kind.GetCategory()).ToList();
        if(categories.Count == 0) {
            return [];
        }

        return new FilteredElementCollector(Doc)
            .WherePasses(new ElementMulticategoryFilter(categories))
            .WhereElementIsNotElementType()
            .ToElements()
            .ToList();
    }
}
