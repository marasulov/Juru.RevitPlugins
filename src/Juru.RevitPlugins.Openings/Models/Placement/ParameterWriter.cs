using Autodesk.Revit.DB;

namespace Juru.RevitPlugins.Openings.Models.Placement;

/// <summary>
/// Записывает значения в параметры экземпляра задания на отверстие.
/// </summary>
public static class ParameterWriter {
    /// <summary>
    /// Записывает длину. Если параметр объявлен длиной, значение уходит во внутренних единицах Revit,
    /// иначе - числом в миллиметрах: в семействах отметки нередко объявлены обычным числом.
    /// </summary>
    /// <returns>True, если параметр найден и доступен для записи.</returns>
    public static bool SetLength(Element element, string paramName, double feetValue) {
        var parameter = element.LookupParameter(paramName);
        if(parameter is null || parameter.IsReadOnly) {
            return false;
        }

        bool isLength = parameter.Definition.GetDataType() == SpecTypeId.Length;
        double value = isLength
            ? feetValue
            : UnitUtils.ConvertFromInternalUnits(feetValue, UnitTypeId.Millimeters);
        return parameter.Set(value);
    }

    /// <summary>
    /// Записывает текст.
    /// </summary>
    /// <returns>True, если параметр найден и доступен для записи.</returns>
    public static bool SetText(Element element, string paramName, string value) {
        var parameter = element.LookupParameter(paramName);
        return parameter is not null && !parameter.IsReadOnly && parameter.Set(value ?? string.Empty);
    }

    /// <summary>
    /// Записывает целое число, в том числе значение параметра типа Да/Нет.
    /// </summary>
    /// <returns>True, если параметр найден и доступен для записи.</returns>
    public static bool SetInteger(Element element, string paramName, int value) {
        var parameter = element.LookupParameter(paramName);
        return parameter is not null && !parameter.IsReadOnly && parameter.Set(value);
    }
}
