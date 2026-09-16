using System.Collections.ObjectModel;
using System.Linq;

using CommunityToolkit.Mvvm.ComponentModel;
using ToolkitCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

using Juru.RevitPlugins.Core.Localization;
using Juru.RevitPlugins.Openings.Models.Configs;

namespace Juru.RevitPlugins.Openings.ViewModels.Settings;

/// <summary>
/// Настройки одного вида инженерных элементов в окне настроек.
/// </summary>
public partial class MepCategoryViewModel : ObservableObject {
    private readonly MepKind _kind;
    private readonly PluginLocalization _localization;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _minDiameterMm;

    [ObservableProperty]
    private string _minWidthMm;

    [ObservableProperty]
    private string _minHeightMm;

    [ObservableProperty]
    private string _maxSizeMm;

    [ObservableProperty]
    private string _sizeRoundingMm;

    [ObservableProperty]
    private string _elevationRoundingMm;

    [ObservableProperty]
    private bool _checkWalls;

    [ObservableProperty]
    private bool _checkFloors;

    [ObservableProperty]
    private OffsetRangeViewModel _selectedOffset;

    public MepCategoryViewModel(MepCategorySettings settings, PluginLocalization localization) {
        _kind = settings.Kind;
        _localization = localization;
        _isSelected = settings.IsSelected;
        _minDiameterMm = settings.MinDiameterMm.ToString("0.##");
        _minWidthMm = settings.MinWidthMm.ToString("0.##");
        _minHeightMm = settings.MinHeightMm.ToString("0.##");
        _maxSizeMm = settings.MaxSizeMm.ToString("0.##");
        _sizeRoundingMm = settings.SizeRoundingMm.ToString();
        _elevationRoundingMm = settings.ElevationRoundingMm.ToString();
        _checkWalls = settings.Structures.Contains(StructureKind.Wall);
        _checkFloors = settings.Structures.Contains(StructureKind.Floor);

        Offsets = new ObservableCollection<OffsetRangeViewModel>(
            settings.Offsets.Select(offset => new OffsetRangeViewModel(offset, localization)));
        _selectedOffset = Offsets.FirstOrDefault();

        AddOffsetCommand = new ToolkitCommand(AddOffset);
        RemoveOffsetCommand = new ToolkitCommand(RemoveOffset, () => SelectedOffset is not null);
    }

    /// <summary>
    /// Название вида для списка.
    /// </summary>
    public string Name => _localization.GetString(GetNameKey(_kind));

    /// <summary>
    /// Круглое ли сечение у элементов этого вида: от этого зависит, какие пороги показывать.
    /// </summary>
    public bool IsRound => _kind.IsRound();

    /// <summary>
    /// Прямоугольное ли сечение: пороги ширины и высоты показываются только для таких видов.
    /// </summary>
    public bool IsRectangular => !IsRound;

    /// <summary>
    /// Диапазоны зазоров.
    /// </summary>
    public ObservableCollection<OffsetRangeViewModel> Offsets { get; }

    public ToolkitCommand AddOffsetCommand { get; }

    public ToolkitCommand RemoveOffsetCommand { get; }

    /// <summary>
    /// Проверяет введённые значения и возвращает текст ошибки, либо <c>null</c>.
    /// </summary>
    public string GetError() {
        if(!IsSelected) {
            return null;
        }

        if(!CheckWalls && !CheckFloors) {
            return _localization.GetString("Settings.Error.NoStructure", Name);
        }
        if(!double.TryParse(MaxSizeMm, out double max) || max <= 0) {
            return _localization.GetString("Settings.Error.MaxSizePositive", Name);
        }
        if(!int.TryParse(SizeRoundingMm, out int sizeRounding) || sizeRounding <= 0) {
            return _localization.GetString("Settings.Error.SizeRoundingPositive", Name);
        }
        if(!int.TryParse(ElevationRoundingMm, out int elevationRounding) || elevationRounding <= 0) {
            return _localization.GetString("Settings.Error.ElevationRoundingPositive", Name);
        }

        string minError = IsRound
            ? Validate(MinDiameterMm, _localization.GetString("Settings.MinDiameterTitle"))
            : Validate(MinWidthMm, _localization.GetString("Settings.MinWidthTitle"))
                ?? Validate(MinHeightMm, _localization.GetString("Settings.MinHeightTitle"));
        if(minError is not null) {
            return minError;
        }

        if(Offsets.Count == 0) {
            return _localization.GetString("Settings.Error.NoOffsets", Name);
        }
        foreach(var offset in Offsets) {
            string error = offset.GetError();
            if(error is not null) {
                return _localization.GetString("Settings.Error.WithDetail", Name, error);
            }
        }
        return null;
    }

    /// <summary>
    /// Собирает настройки из введённых значений.
    /// </summary>
    public MepCategorySettings ToSettings() {
        var structures = new System.Collections.Generic.List<StructureKind>();
        if(CheckWalls) { structures.Add(StructureKind.Wall); }
        if(CheckFloors) { structures.Add(StructureKind.Floor); }

        return new MepCategorySettings {
            Kind = _kind,
            IsSelected = IsSelected,
            MinDiameterMm = ParseOrZero(MinDiameterMm),
            MinWidthMm = ParseOrZero(MinWidthMm),
            MinHeightMm = ParseOrZero(MinHeightMm),
            MaxSizeMm = ParseOrZero(MaxSizeMm),
            SizeRoundingMm = int.Parse(SizeRoundingMm),
            ElevationRoundingMm = int.Parse(ElevationRoundingMm),
            Structures = structures,
            Offsets = Offsets.Select(offset => offset.ToRange()).ToList()
        };
    }

    private string Validate(string value, string title) {
        return double.TryParse(value, out double parsed) && parsed >= 0
            ? null
            : _localization.GetString("Settings.Error.FieldNonNegative", Name, title);
    }

    private static double ParseOrZero(string value) {
        return double.TryParse(value, out double parsed) ? parsed : 0;
    }

    private void AddOffset() {
        double from = Offsets.Count == 0 ? 0 : ParseOrZero(Offsets[Offsets.Count - 1].ToMm);
        var added = new OffsetRangeViewModel(new OffsetRange {
            FromMm = from,
            ToMm = from + 100,
            OffsetMm = 50,
            Shape = OpeningShape.Rectangle
        }, _localization);
        Offsets.Add(added);
        SelectedOffset = added;
    }

    private void RemoveOffset() {
        var removed = SelectedOffset;
        if(removed is null) {
            return;
        }
        Offsets.Remove(removed);
        SelectedOffset = Offsets.FirstOrDefault();
    }

    private static string GetNameKey(MepKind kind) {
        switch(kind) {
            case MepKind.Pipe:
                return "Settings.MepKind.Pipe";
            case MepKind.RoundDuct:
                return "Settings.MepKind.RoundDuct";
            case MepKind.RectangleDuct:
                return "Settings.MepKind.RectangleDuct";
            case MepKind.CableTray:
                return "Settings.MepKind.CableTray";
            case MepKind.Conduit:
                return "Settings.MepKind.Conduit";
            default:
                return kind.ToString();
        }
    }
}
