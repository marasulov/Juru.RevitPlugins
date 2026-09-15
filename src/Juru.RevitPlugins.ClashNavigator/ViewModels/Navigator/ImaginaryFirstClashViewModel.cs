using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Dynamic;
using System.Windows.Input;

using Autodesk.Revit.DB;

using dosymep.SimpleServices;
using dosymep.WPF.Commands;
using dosymep.WPF.ViewModels;

using Juru.RevitPlugins.ClashNavigator.Models;
using Juru.RevitPlugins.ClashNavigator.Models.Clashes;
using Juru.RevitPlugins.ClashNavigator.Models.Extensions;

namespace Juru.RevitPlugins.ClashNavigator.ViewModels.Navigator;

/// <summary>
/// Несуществующая коллизия, в которой есть только первый элемент
/// </summary>
internal class ImaginaryFirstClashViewModel
    : BaseViewModel, IClashViewModel, IEquatable<ImaginaryFirstClashViewModel> {
    private readonly RevitRepository _revitRepository;
    private readonly ElementViewModel _firstElement;
    private readonly ILocalizationService _localizationService;

    public ImaginaryFirstClashViewModel(
        RevitRepository revitRepository,
        ElementViewModel firstElement,
        ILocalizationService localizationService) {
        _localizationService = localizationService
            ?? throw new ArgumentNullException(nameof(localizationService));

        _revitRepository = revitRepository ?? throw new ArgumentNullException(nameof(revitRepository));
        _firstElement = firstElement ?? throw new ArgumentNullException(nameof(firstElement));
        ClashStatusName = ClashStatus.GetDisplayName(localizationService);
        FirstElementVolume = firstElement.ElementVolume;

        FirstId = _firstElement.Element.Id;
        FirstObjectName = _firstElement.Element.ObjectName;
        FirstCategory = _firstElement.Element.Category;
        FirstTypeName = _firstElement.Element.Name;
        FirstFamilyName = _firstElement.Element.FamilyName;
        FirstDocumentName = _firstElement.Element.DocumentName;
        FirstLevel = _firstElement.Element.Level;
        FirstElementParams = new ExpandoObject();
        SecondElementParams = new ExpandoObject();

        AddCommentCommand = RelayCommand.Create(() => { }, () => false);
        RemoveCommentCommand = RelayCommand.Create(() => { }, () => false);
    }

    public ICommand AddCommentCommand { get; }
    public ICommand RemoveCommentCommand { get; }

    public ClashCommentViewModel SelectedComment {
        get => null;
        set { return; }
    }

    public string CommentsTitle => ClashName;

    public bool CanEditComments {
        get => false;
        set { return; }
    }

    public ObservableCollection<ClashCommentViewModel> Comments { get; } = [];


    public ClashStatus ClashStatus { get => ClashStatus.Imaginary; set { return; } }

    public string ClashStatusName { get; }

    public IReadOnlyCollection<ClashStatusViewModel> Statuses { get; } = [];

    // "Нет пересечения" - результат локального пересчёта геометрии, статус такой коллизии не меняют.
    public bool CanChangeStatus => false;

    public string StatusChangedBy => string.Empty;

    public string StatusAuthorInfo => string.Empty;

    public string RevitHint => _localizationService.GetLocalizedString("Clash.ImaginaryHint");

    public string StatusChangedAt => string.Empty;

    public string ClashName { get => string.Empty; set { return; } }

    public ElementId FirstId { get; }

    public string FirstObjectName { get; }

    public string FirstTypeName { get; }

    public string FirstFamilyName { get; }

    public string FirstDocumentName { get; }

    public string FirstLevel { get; }

    public string FirstCategory { get; }

    public ElementId SecondId => ElementId.InvalidElementId;

    public string SecondObjectName => string.Empty;

    public ExpandoObject FirstElementParams { get; }

    public ExpandoObject SecondElementParams { get; }

    public string SecondTypeName => string.Empty;

    public string SecondFamilyName => string.Empty;

    public string SecondLevel => string.Empty;

    public string SecondDocumentName => string.Empty;

    public string SecondCategory => string.Empty;

    public double FirstElementIntersectionPercentage => 0;

    public double SecondElementIntersectionPercentage => 0;

    public double IntersectionVolume => 0;

    public double FirstElementVolume { get; }

    public double SecondElementVolume => 0;

    public bool ClashDataIsValid => true;


    public ElementModel GetFirstElement() {
        return _firstElement.Element;
    }

    public ElementModel GetSecondElement() {
        throw new NotSupportedException();
    }

    public ICollection<ElementModel> GetElements() {
        return [GetFirstElement()];
    }

    public bool Equals(ImaginaryFirstClashViewModel other) {
        if(other is null) { return false; }
        return ReferenceEquals(this, other) || _firstElement.Equals(other._firstElement);
    }

    public override bool Equals(object obj) {
        return Equals(obj as ImaginaryFirstClashViewModel);
    }

    public override int GetHashCode() {
        return 1531809236 + EqualityComparer<ElementViewModel>.Default.GetHashCode(_firstElement);
    }

    public void SetElementParams(string[] paramNames) {
        ((IDictionary<string, object>) SecondElementParams).Clear();
        var firstElementParams = (IDictionary<string, object>) FirstElementParams;
        firstElementParams.Clear();

        var firstElement = GetFirstElement().GetElement(_revitRepository.DocInfos);
        if(firstElement is not null) {
            for(int i = 0; i < paramNames.Length; i++) {
                firstElementParams.Add($"{ClashViewModel.ElementParamFieldName}{i}",
                    firstElement.GetParamValueAsString(paramNames[i]));
            }
        }
    }
}
