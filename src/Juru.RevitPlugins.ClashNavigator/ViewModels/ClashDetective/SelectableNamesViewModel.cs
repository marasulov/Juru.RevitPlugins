using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;

using dosymep.WPF.ViewModels;

using Juru.RevitPlugins.ClashNavigator.Models;

namespace Juru.RevitPlugins.ClashNavigator.ViewModels.ClashDetective;
internal class SelectableNamesViewModel : BaseViewModel {
    private string _title;
    private bool _allSelected;
    private string _searchText;

    public SelectableNamesViewModel(IEnumerable<IName> allEntities, IEnumerable<IName> currentSelection) {
        if(allEntities is null) {
            throw new ArgumentNullException(nameof(allEntities));
        }

        if(currentSelection is null) {
            throw new ArgumentNullException(nameof(currentSelection));
        }

        Entities = new ObservableCollection<SelectableNameViewModel>(
            allEntities.Select(e => new SelectableNameViewModel(e)));
        foreach(var entity in Entities) {
            if(currentSelection.Any(s => entity.Name.Equals(s.Name, StringComparison.CurrentCultureIgnoreCase))) {
                entity.IsSelected = true;
            }
        }

        EntitiesView = CollectionViewSource.GetDefaultView(Entities);
        EntitiesView.Filter = item => IsVisible((SelectableNameViewModel) item);
    }

    public string Title {
        get => _title;
        set => RaiseAndSetIfChanged(ref _title, value);
    }

    /// <summary>
    /// Отмечает или снимает все строки, видимые при текущем поиске.
    /// </summary>
    /// <remarks>
    /// Только видимые: "найти 'Duct' и выбрать все" должно выбрать воздуховоды, а не все
    /// категории модели, которых в списке сейчас даже не видно.
    /// </remarks>
    public bool AllSelected {
        get => _allSelected;
        set {
            if(_allSelected != value) {
                RaiseAndSetIfChanged(ref _allSelected, value);
                foreach(var entity in Entities.Where(IsVisible)) {
                    entity.IsSelected = value;
                }
            }
        }
    }

    /// <summary>
    /// Строка поиска: в списке остаются имена, где она встречается, без учёта регистра.
    /// </summary>
    public string SearchText {
        get => _searchText;
        set {
            RaiseAndSetIfChanged(ref _searchText, value);
            EntitiesView.Refresh();
        }
    }

    public ObservableCollection<SelectableNameViewModel> Entities { get; }

    /// <summary>
    /// <see cref="Entities" /> с фильтром по <see cref="SearchText" /> - то, что показывает список.
    /// </summary>
    public ICollectionView EntitiesView { get; }

    public ICollection<T> GetSelectedEntities<T>() where T : IName {
        return [.. Entities.Where(e => e.IsSelected).Select(e => e.Entity).OfType<T>()];
    }

    private bool IsVisible(SelectableNameViewModel entity) {
        return string.IsNullOrWhiteSpace(SearchText)
               || entity.Name.IndexOf(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0;
    }
}
