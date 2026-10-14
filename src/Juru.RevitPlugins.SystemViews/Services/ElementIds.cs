using Autodesk.Revit.DB;

namespace Juru.RevitPlugins.SystemViews.Services;

/// <summary>
///     Перевод <see cref="ElementId" /> в число и обратно: в Ревите 2024 id стал 64-битным.
/// </summary>
internal static class ElementIds
{
    public static long ToLong(ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }

    public static ElementId FromLong(long value)
    {
#if REVIT2024_OR_GREATER
        return new ElementId(value);
#else
        return new ElementId((int) value);
#endif
    }
}
