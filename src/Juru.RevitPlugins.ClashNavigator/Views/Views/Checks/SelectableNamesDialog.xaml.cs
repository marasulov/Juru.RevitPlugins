using Juru.RevitPlugins.ClashNavigator.ViewModels.ClashDetective;

using Wpf.Ui;

namespace Juru.RevitPlugins.ClashNavigator.Views.Checks;

internal partial class SelectableNamesDialog {
    public SelectableNamesDialog(SelectableNamesViewModel vm, IContentDialogService dialogService)
        : base(dialogService.GetDialogHost()) {
        InitializeComponent();
        ViewModel = vm;
        DataContext = vm;
    }

    public SelectableNamesViewModel ViewModel { get; }
}
