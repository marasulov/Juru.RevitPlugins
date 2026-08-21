using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

namespace Juru.RevitPlugins.Openings.Models.Navigator;

/// <summary>
/// Разбивает инженерные элементы, проходящие через одно задание, на отдельные трассы.
/// </summary>
/// <remarks>
/// Считать проходы по числу элементов нельзя: воздуховод с отводом и врезкой - это три элемента
/// и один проход. Замер на модели: из 26 заданий, через которые проходило больше одного элемента,
/// 15 были одной трассой со своей арматурой, и все они оставались без проверки габаритов, потому что
/// объединённым заданиям её не делают. Трассы различаются по коннекторам: соединённые элементы -
/// это одна трасса, а две параллельные трубы разных систем - две.
/// </remarks>
public static class MepPassGrouping {
    /// <summary>
    /// Возвращает группы соединённых между собой элементов: одна группа - один проход.
    /// </summary>
    public static IReadOnlyCollection<IReadOnlyCollection<Element>> GetPasses(
        IReadOnlyCollection<Element> elements) {

        if(elements is null || elements.Count == 0) {
            return [];
        }

        var owner = new Dictionary<ElementId, ElementId>();
        foreach(var element in elements) {
            owner[element.Id] = element.Id;
        }

        foreach(var element in elements) {
            foreach(var neighbour in GetConnected(element)) {
                if(owner.ContainsKey(neighbour)) {
                    Join(owner, element.Id, neighbour);
                }
            }
        }

        return elements
            .GroupBy(element => Find(owner, element.Id))
            .Select(group => (IReadOnlyCollection<Element>) group.ToList())
            .ToList();
    }

    /// <summary>
    /// Возвращает id элементов, присоединённых к данному через коннекторы.
    /// </summary>
    private static IEnumerable<ElementId> GetConnected(Element element) {
        var manager = GetConnectorManager(element);
        if(manager is null) {
            yield break;
        }

        foreach(Connector connector in manager.Connectors) {
            foreach(Connector reference in connector.AllRefs) {
                var neighbour = reference.Owner;
                if(neighbour is not null && neighbour.Id != element.Id) {
                    yield return neighbour.Id;
                }
            }
        }
    }

    private static ConnectorManager? GetConnectorManager(Element element) {
        return element switch {
            MEPCurve curve => curve.ConnectorManager,
            FamilyInstance instance => instance.MEPModel?.ConnectorManager,
            _ => null
        };
    }

    private static ElementId Find(Dictionary<ElementId, ElementId> owner, ElementId id) {
        while(owner[id] != id) {
            id = owner[id] = owner[owner[id]];
        }
        return id;
    }

    private static void Join(Dictionary<ElementId, ElementId> owner, ElementId first, ElementId second) {
        var firstRoot = Find(owner, first);
        var secondRoot = Find(owner, second);
        if(firstRoot != secondRoot) {
            owner[firstRoot] = secondRoot;
        }
    }
}
