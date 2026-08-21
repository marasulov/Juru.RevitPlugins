using CommunityToolkit.Mvvm.ComponentModel;

using Juru.RevitPlugins.Openings.Models.Configs;

namespace Juru.RevitPlugins.Openings.ViewModels.Settings;

/// <summary>
/// Строка таблицы зазоров: диапазон габаритов элемента, зазор и форма отверстия.
/// </summary>
public partial class OffsetRangeViewModel : ObservableObject {
    [ObservableProperty]
    private string _fromMm;

    [ObservableProperty]
    private string _toMm;

    [ObservableProperty]
    private string _offsetMm;

    [ObservableProperty]
    private OpeningShape _shape;

    public OffsetRangeViewModel(OffsetRange range) {
        _fromMm = range.FromMm.ToString("0.##");
        _toMm = range.ToMm.ToString("0.##");
        _offsetMm = range.OffsetMm.ToString("0.##");
        _shape = range.Shape;
    }

    /// <summary>
    /// Проверяет строку и возвращает текст ошибки, либо <c>null</c>, если всё в порядке.
    /// </summary>
    public string GetError() {
        if(!double.TryParse(FromMm, out double from) || from < 0) {
            return "Начало диапазона должно быть неотрицательным числом.";
        }
        if(!double.TryParse(ToMm, out double to) || to <= from) {
            return "Конец диапазона должен быть больше начала.";
        }
        if(!double.TryParse(OffsetMm, out double offset) || offset < 0) {
            return "Зазор должен быть неотрицательным числом.";
        }
        return null;
    }

    /// <summary>
    /// Собирает настройку из введённых значений.
    /// </summary>
    public OffsetRange ToRange() {
        return new OffsetRange {
            FromMm = double.Parse(FromMm),
            ToMm = double.Parse(ToMm),
            OffsetMm = double.Parse(OffsetMm),
            Shape = Shape
        };
    }
}
