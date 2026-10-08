using System;
using System.Linq;

using Autodesk.Revit.DB;

namespace Juru.RevitPlugins.ClashNavigator.Models.ClashDetection;
/// <summary>
/// Правила, по которым пересечение не считается коллизией.
/// </summary>
/// <remarks>
/// Аналог правил Clash Detective в Navisworks ("элементы одного файла", "части одного
/// составного объекта"), но по связям Ревита, которых Navisworks не видит: изоляция знает свою
/// трубу, дверь - свою стену, фитинг - соединённую трубу. Такие пары пересекаются всегда и только
/// засоряют отчёт.
/// </remarks>
[Flags]
internal enum ClashIgnoreRules {
    None = 0,

    /// <summary>
    /// Изоляция или облицовка и труба, воздуховод или фитинг, на которых она лежит.
    /// </summary>
    Insulation = 1,

    /// <summary>
    /// Элемент и его основа (дверь и стена), вложенные семейства одного родителя.
    /// </summary>
    HostAndNested = 2,

    /// <summary>
    /// Элементы, соединённые через коннекторы: труба и её фитинг.
    /// </summary>
    Connected = 4,

    /// <summary>
    /// Элементы одной инженерной системы.
    /// </summary>
    SameSystem = 8,

    /// <summary>
    /// Оба элемента из одного файла.
    /// </summary>
    SameFile = 16,

    /// <summary>
    /// Правила по умолчанию для новой проверки: те, что отсекают только заведомый мусор.
    /// </summary>
    Default = Insulation | HostAndNested | Connected
}

/// <summary>
/// Проверка пары элементов по правилам исключения.
/// </summary>
internal static class ClashIgnoreRuleMatcher {
    /// <summary>
    /// Пару надо пропустить. Элементы сравниваются только внутри одного документа: связи между
    /// элементами разных файлов (основа, коннекторы, изоляция) в Ревите не существует.
    /// </summary>
    public static bool ShouldIgnore(Element first, Element second, ClashIgnoreRules rules) {
        if(rules == ClashIgnoreRules.None || first is null || second is null) {
            return false;
        }

        bool sameDocument = first.Document.Equals(second.Document);
        if(rules.HasFlag(ClashIgnoreRules.SameFile) && sameDocument) {
            return true;
        }
        if(!sameDocument) {
            return false;
        }

        return rules.HasFlag(ClashIgnoreRules.Insulation) && IsInsulationOf(first, second)
               || rules.HasFlag(ClashIgnoreRules.HostAndNested) && IsHostOrNested(first, second)
               || rules.HasFlag(ClashIgnoreRules.Connected) && AreConnected(first, second)
               || rules.HasFlag(ClashIgnoreRules.SameSystem) && InSameSystem(first, second);
    }

    private static bool IsInsulationOf(Element first, Element second) {
        return first is InsulationLiningBase firstLining && firstLining.HostElementId == second.Id
               || second is InsulationLiningBase secondLining && secondLining.HostElementId == first.Id
               || first is InsulationLiningBase a && second is InsulationLiningBase b && a.HostElementId == b.HostElementId;
    }

    private static bool IsHostOrNested(Element first, Element second) {
        var firstInstance = first as FamilyInstance;
        var secondInstance = second as FamilyInstance;
        return firstInstance?.Host?.Id == second.Id
               || secondInstance?.Host?.Id == first.Id
               || firstInstance?.SuperComponent?.Id == second.Id
               || secondInstance?.SuperComponent?.Id == first.Id
               || firstInstance?.SuperComponent is { } parent
               && secondInstance?.SuperComponent?.Id == parent.Id;
    }

    private static bool AreConnected(Element first, Element second) {
        var connectors = GetConnectors(first);
        if(connectors is null) {
            return false;
        }

        foreach(Connector connector in connectors) {
            if(!connector.IsConnected) {
                continue;
            }
            foreach(Connector reference in connector.AllRefs) {
                if(reference.Owner?.Id == second.Id) {
                    return true;
                }
            }
        }
        return false;
    }

    private static ConnectorSet GetConnectors(Element element) {
        try {
            return element switch {
                MEPCurve curve => curve.ConnectorManager?.Connectors,
                FamilyInstance instance => instance.MEPModel?.ConnectorManager?.Connectors,
                FabricationPart part => part.ConnectorManager?.Connectors,
                _ => null
            };
        } catch(Autodesk.Revit.Exceptions.ApplicationException) {
            return null;
        }
    }

    private static bool InSameSystem(Element first, Element second) {
        string firstSystem = GetSystemName(first);
        return !string.IsNullOrWhiteSpace(firstSystem)
               && string.Equals(firstSystem, GetSystemName(second), StringComparison.CurrentCultureIgnoreCase);
    }

    private static string GetSystemName(Element element) {
        return element.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString();
    }
}
