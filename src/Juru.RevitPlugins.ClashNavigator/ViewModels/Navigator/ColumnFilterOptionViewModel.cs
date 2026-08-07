using dosymep.WPF.ViewModels;

namespace Juru.RevitPlugins.ClashNavigator.ViewModels.Navigator;

internal class ColumnFilterOptionViewModel : BaseViewModel {
    private bool _isChecked;

    public string Value { get; set; }

    public bool IsChecked {
        get => _isChecked;
        set => RaiseAndSetIfChanged(ref _isChecked, value);
    }
}
