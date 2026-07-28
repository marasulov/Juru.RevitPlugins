using System;

using dosymep.WPF.ViewModels;

using Juru.RevitPlugins.ClashNavigator.Models;

namespace Juru.RevitPlugins.ClashNavigator.ViewModels.ClashDetective;
internal class SelectableNameViewModel : BaseViewModel, IName {
    private bool _isSelected;

    public SelectableNameViewModel(IName namedEntity) {
        Entity = namedEntity ?? throw new ArgumentNullException(nameof(namedEntity));
    }

    public IName Entity { get; }

    public string Name => Entity.Name;

    public bool IsSelected {
        get => _isSelected;
        set => RaiseAndSetIfChanged(ref _isSelected, value);
    }
}
