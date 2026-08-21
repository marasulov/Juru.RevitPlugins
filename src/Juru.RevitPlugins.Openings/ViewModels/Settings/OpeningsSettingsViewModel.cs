using System.Collections.ObjectModel;
using System.Linq;

using CommunityToolkit.Mvvm.ComponentModel;
using ToolkitCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

using Juru.RevitPlugins.Openings.Models.Configs;

namespace Juru.RevitPlugins.Openings.ViewModels.Settings;

/// <summary>
/// Окно настроек выдачи заданий на отверстия.
/// </summary>
public partial class OpeningsSettingsViewModel : ObservableObject {
    private readonly OpeningsConfig _config;

    [ObservableProperty]
    private MepCategoryViewModel _selectedCategory;

    [ObservableProperty]
    private string _unitedTaskSizeRoundingMm;

    [ObservableProperty]
    private string _unitedTaskMaxGapMm;

    [ObservableProperty]
    private string _unitedTaskTouchToleranceMm;

    [ObservableProperty]
    private string _minPassDepthMm;

    [ObservableProperty]
    private bool _showPlacingErrors;

    [ObservableProperty]
    private string _errorText;

    public OpeningsSettingsViewModel(OpeningsConfig config) {
        _config = config;

        // Порядок как в окне Bim4Everyone: трубы, воздуховоды, лотки, короба.
        var order = new[] {
            MepKind.Pipe, MepKind.RectangleDuct, MepKind.RoundDuct, MepKind.CableTray, MepKind.Conduit
        };
        Categories = new ObservableCollection<MepCategoryViewModel>(
            config.Categories
                .OrderBy(category => System.Array.IndexOf(order, category.Kind))
                .Select(category => new MepCategoryViewModel(category)));
        _selectedCategory = Categories.FirstOrDefault();

        _unitedTaskSizeRoundingMm = config.UnitedTaskSizeRoundingMm.ToString();
        _unitedTaskMaxGapMm = config.UnitedTaskMaxGapMm.ToString();
        _unitedTaskTouchToleranceMm = config.UnitedTaskTouchToleranceMm.ToString();
        _minPassDepthMm = config.MinPassDepthMm.ToString("0.##");
        _showPlacingErrors = config.ShowPlacingErrors;

        AcceptViewCommand = new ToolkitCommand(Accept, CanAccept);
        foreach(var category in Categories) {
            category.PropertyChanged += (_, _) => Revalidate();
        }
        Revalidate();
    }

    /// <summary>
    /// Виды инженерных элементов.
    /// </summary>
    public ObservableCollection<MepCategoryViewModel> Categories { get; }

    public ToolkitCommand AcceptViewCommand { get; }

    private void Accept() {
        _config.Categories = Categories.Select(category => category.ToSettings()).ToList();
        _config.UnitedTaskSizeRoundingMm = int.Parse(UnitedTaskSizeRoundingMm);
        _config.UnitedTaskMaxGapMm = int.Parse(UnitedTaskMaxGapMm);
        _config.UnitedTaskTouchToleranceMm = int.Parse(UnitedTaskTouchToleranceMm);
        _config.MinPassDepthMm = double.Parse(MinPassDepthMm);
        _config.ShowPlacingErrors = ShowPlacingErrors;
        _config.SaveProjectConfig();
    }

    private bool CanAccept() {
        return string.IsNullOrEmpty(ErrorText);
    }

    /// <summary>
    /// Проверяет введённое и держит текст ошибки в актуальном состоянии: пока он не пуст,
    /// кнопка сохранения недоступна.
    /// </summary>
    private void Revalidate() {
        ErrorText = GetError();
        AcceptViewCommand.NotifyCanExecuteChanged();
    }

    private string GetError() {
        if(Categories.All(category => !category.IsSelected)) {
            return "Не выбран ни один вид инженерных элементов.";
        }
        if(!int.TryParse(UnitedTaskSizeRoundingMm, out int rounding) || rounding <= 0) {
            return "Округление объединённых заданий должно быть положительным числом.";
        }
        if(!int.TryParse(UnitedTaskMaxGapMm, out int gap) || gap < 0) {
            return "Зазор склейки по толщине должен быть неотрицательным числом.";
        }
        if(!int.TryParse(UnitedTaskTouchToleranceMm, out int touch) || touch < 0) {
            return "Допуск касания должен быть неотрицательным числом.";
        }
        if(!double.TryParse(MinPassDepthMm, out double depth) || depth < 0) {
            return "Наименьшая глубина прохода должна быть неотрицательным числом.";
        }

        return Categories.Select(category => category.GetError()).FirstOrDefault(error => error is not null);
    }

    partial void OnUnitedTaskSizeRoundingMmChanged(string value) => Revalidate();

    partial void OnUnitedTaskMaxGapMmChanged(string value) => Revalidate();

    partial void OnUnitedTaskTouchToleranceMmChanged(string value) => Revalidate();

    partial void OnMinPassDepthMmChanged(string value) => Revalidate();
}
