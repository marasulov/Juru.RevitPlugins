using System.Text.RegularExpressions;

using Autodesk.Revit.DB;

namespace Juru.RevitPlugins.ClashNavigator.Models.RevitClashReport;
internal abstract class BaseClashesLoader {
    protected BaseClashesLoader() { }


    /// <summary>
    /// Возвращает Id или InvalidElementId, если не удалось преобразовать строку
    /// </summary>
    private protected ElementId GetId(string idString) => ParseElementId(idString);

    /// <summary>
    /// Возвращает Id или InvalidElementId, если не удалось преобразовать строку.
    /// Используется загрузчиками отчётов, для которых Id элемента приходит как произвольная строка
    /// (например, "Объект Id" из Navisworks или elementIdA/elementIdB с сервера коллизий).
    /// </summary>
    internal static ElementId ParseElementId(string idString) {
        if(string.IsNullOrWhiteSpace(idString)) { return ElementId.InvalidElementId; }

        var match = Regex.Match(idString, @"(?'id'\d+)");
        if(match.Success) {
            string stringId = match.Groups["id"].Value;
#if REVIT_2023_OR_LESS
            bool successParse = int.TryParse(stringId, out int numericId);
#else
            bool successParse = long.TryParse(stringId, out long numericId);
#endif
            return successParse ? new ElementId(numericId) : ElementId.InvalidElementId;
        }
        return ElementId.InvalidElementId;
    }
}