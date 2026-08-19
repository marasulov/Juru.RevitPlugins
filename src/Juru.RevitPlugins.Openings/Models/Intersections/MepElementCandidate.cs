using Autodesk.Revit.DB;

using Juru.RevitPlugins.Openings.Models.Configs;

namespace Juru.RevitPlugins.Openings.Models.Intersections;

/// <summary>
/// Инженерный элемент, отобранный для поиска пересечений, вместе с настройками своего вида.
/// </summary>
public class MepElementCandidate {
    public MepElementCandidate(Element element, MepCategorySettings settings, MepElementSize size, bool isFitting) {
        Element = element;
        Settings = settings;
        Size = size;
        IsFitting = isFitting;
    }

    /// <summary>
    /// Инженерный элемент.
    /// </summary>
    public Element Element { get; }

    /// <summary>
    /// Настройки вида, по которым элемент отобран.
    /// </summary>
    public MepCategorySettings Settings { get; }

    /// <summary>
    /// Габариты сечения элемента.
    /// </summary>
    public MepElementSize Size { get; }

    /// <summary>
    /// Соединительная арматура (отвод, тройник, переход), а не линейный элемент.
    /// </summary>
    public bool IsFitting { get; }
}
