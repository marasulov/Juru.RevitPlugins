using System.Collections.Generic;

using Autodesk.Revit.DB;

namespace Juru.RevitPlugins.Openings.Models.Intersections;

/// <summary>
/// Поставщик элементов одного документа для поиска пересечений.
/// </summary>
public interface IElementsProvider {
    /// <summary>
    /// Документ, из которого берутся элементы.
    /// </summary>
    Document Doc { get; }

    /// <summary>
    /// Преобразование документа относительно активного файла.
    /// </summary>
    Transform MainTransform { get; }

    /// <summary>
    /// Возвращает элементы для проверки на пересечения.
    /// </summary>
    IReadOnlyCollection<Element> GetElements();
}
