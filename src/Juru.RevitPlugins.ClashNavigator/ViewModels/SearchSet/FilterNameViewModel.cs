using System;
using System.Collections.Generic;
using System.Linq;

using dosymep.SimpleServices;
using dosymep.WPF.ViewModels;

namespace Juru.RevitPlugins.ClashNavigator.ViewModels.SearchSet;
/// <summary>
/// Запрос имени набора - при переименовании и копировании.
/// </summary>
internal class FilterNameViewModel : BaseViewModel {
    private readonly ILocalizationService _localizationService;
    private readonly HashSet<string> _takenNames;
    private string _name;
    private string _errorText;

    /// <param name="title">Заголовок окна запроса.</param>
    /// <param name="takenNames">Имена, которые уже заняты другими наборами.</param>
    /// <param name="name">Имя, с которым окно открывается.</param>
    public FilterNameViewModel(ILocalizationService localizationService,
        string title,
        IEnumerable<string> takenNames,
        string name) {

        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        if(takenNames is null) { throw new ArgumentNullException(nameof(takenNames)); }

        Title = title;
        // Как и проверка имён при сохранении (FiltersViewModel.CanSave), без учёта регистра.
        _takenNames = new HashSet<string>(takenNames.Where(item => item != null), StringComparer.CurrentCultureIgnoreCase);
        Name = name;
    }

    public string Title { get; }

    public string Name {
        get => _name;
        set {
            RaiseAndSetIfChanged(ref _name, value);
            ErrorText = GetErrorText();
            RaisePropertyChanged(nameof(CanAccept));
        }
    }

    public string ErrorText {
        get => _errorText;
        private set => RaiseAndSetIfChanged(ref _errorText, value);
    }

    public bool CanAccept => ErrorText is null;

    private string GetErrorText() {
        if(string.IsNullOrWhiteSpace(Name)) {
            return _localizationService.GetLocalizedString("SearchSets.Validation.NoName");
        }
        return _takenNames.Contains(Name.Trim())
            ? _localizationService.GetLocalizedString("SearchSet.NameTaken")
            : null;
    }
}
