using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Dynamic;
using System.Linq;
using System.Windows.Input;

using Autodesk.Revit.DB;

using dosymep.WPF.Commands;
using dosymep.WPF.ViewModels;
using dosymep.SimpleServices;

using Juru.RevitPlugins.ClashNavigator.Models;
using Juru.RevitPlugins.ClashNavigator.Models.Clashes;
using Juru.RevitPlugins.ClashNavigator.Models.Extensions;
using Juru.RevitPlugins.ClashNavigator.Models.RevitClashReport.ServerClashes;

namespace Juru.RevitPlugins.ClashNavigator.ViewModels.Navigator;
internal class ClashViewModel : BaseViewModel, IClashViewModel, IEquatable<ClashViewModel> {
    public const string ElementParamFieldName = "Field";
    private ClashStatus _clashStatus;
    private string _clashName;
    private string _statusChangedBy;
    private string _statusChangedAt;
    private readonly Action _statusChanged;
    private readonly RevitRepository _revitRepository;
    private readonly ILocalizationService _localizationService;
    private bool _clashDataIsValid;
    private ClashCommentViewModel _selectedComment;
    private bool _canEditComments = true;

    /// <param name="statusChanged">
    /// Вызывается, когда пользователь изменил статус коллизии. Отчёт по этому вызову обновляет счётчик
    /// неотправленных изменений: подписка только на выделенную коллизию его пропускала.
    /// </param>
    public ClashViewModel(
        RevitRepository revitRepository,
        ClashModel clash,
        ILocalizationService localizationService,
        IReadOnlyCollection<ClashStatusViewModel> statuses = null,
        Action statusChanged = null) {

        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _revitRepository = revitRepository;
        _statusChanged = statusChanged;
        Statuses = statuses ?? [];

        ClashName = clash.Name;
        Comments = [..clash.Comments?.Select(c => new ClashCommentViewModel(_revitRepository, c)) ?? []];
        SelectedComment = Comments.FirstOrDefault();

        FirstId = clash.MainElement.Id;
        FirstObjectName = clash.MainElement.ObjectName;
        FirstCategory = clash.MainElement.Category;
        FirstTypeName = clash.MainElement.Name;
        FirstFamilyName = clash.MainElement.FamilyName;
        FirstDocumentName = clash.MainElement.DocumentName;
        FirstLevel = clash.MainElement.Level;
        FirstElementParams = new ExpandoObject();

        SecondId = clash.OtherElement.Id;
        SecondObjectName = clash.OtherElement.ObjectName;
        SecondCategory = clash.OtherElement.Category;
        SecondTypeName = clash.OtherElement.Name;
        SecondFamilyName = clash.OtherElement.FamilyName;
        SecondLevel = clash.OtherElement.Level;
        SecondDocumentName = clash.OtherElement.DocumentName;
        SecondElementParams = new ExpandoObject();

        SetIntersectionData(clash);

        ClashStatus = clash.ClashStatus;
        Clash = clash;
        Clash.SetRevitRepository(_revitRepository);
        SetStatusAuthor(clash.StatusChangedBy, clash.StatusChangedAt);

        AddCommentCommand = RelayCommand.Create(AddComment);
        RemoveCommentCommand = RelayCommand.Create<ClashCommentViewModel>(RemoveComment, CanRemoveComment);
    }

    public ICommand AddCommentCommand { get; }

    public ICommand RemoveCommentCommand { get; }

    public ClashStatus ClashStatus {
        get => _clashStatus;
        set {
            if(value != ClashStatus.Imaginary && value != _clashStatus) {
                RaiseAndSetIfChanged(ref _clashStatus, value);
                RaisePropertyChanged(nameof(ClashStatusName));
                RaisePropertyChanged(nameof(HasUnsentStatus));
                RaisePropertyChanged(nameof(StatusAuthorInfo));
                _statusChanged?.Invoke();
            }
        }
    }

    /// <summary>
    /// Статус изменён пользователем, но ещё не отправлен на сервер коллизий.
    /// Отправка идёт по кнопке сохранения отчёта.
    /// </summary>
    /// <summary>
    /// Статус отличается от того, что известно о коллизии на сервере, то есть ждёт отправки.
    /// Считается по данным модели, а не по флагу в памяти: флаг терялся при сохранении отчёта
    /// и перезапуске Ревита, и правки пользователя становилось нечем отличить от статусов из базы.
    /// </summary>
    public bool HasUnsentStatus => Clash?.ServerId is not null
        && ServerClashStatus.ToClashStatus(Clash.ServerStatus) != ClashStatus;

    /// <summary>
    /// Содержимое колонки "Кем изменён". Заполнено всегда: по строке должно быть видно,
    /// ждёт ли статус отправки, кто его изменил в базе или что связи с базой нет вовсе.
    /// Пустая ячейка не позволяла отличить эти состояния друг от друга.
    /// </summary>
    public string StatusAuthorInfo {
        get {
            if(HasUnsentStatus) {
                return $"{WindowsUser.DisplayName} — не отправлено";
            }
            // Имя сотрудника нужнее логина, но если сервер имени не вернул - показываем учётную запись.
            if(!string.IsNullOrWhiteSpace(Clash?.StatusChangedByName)) {
                return Clash.StatusChangedByName;
            }
            if(!string.IsNullOrWhiteSpace(StatusChangedBy)) {
                return StatusChangedBy;
            }
            return Clash?.ServerId is null
                ? "нет связи с базой"
                : $"в базе: {Clash.ServerStatus ?? "неизвестно"}";
        }
    }

    public string ClashStatusName => ClashStatus.GetDisplayName();

    public IReadOnlyCollection<ClashStatusViewModel> Statuses { get; }

    /// <summary>
    /// Статус реальной коллизии пользователь меняет прямо в таблице.
    /// </summary>
    public bool CanChangeStatus => true;

    public string StatusChangedBy {
        get => _statusChangedBy;
        private set {
            RaiseAndSetIfChanged(ref _statusChangedBy, value);
            RaisePropertyChanged(nameof(StatusAuthorInfo));
        }
    }

    public string StatusChangedAt {
        get => _statusChangedAt;
        private set => RaiseAndSetIfChanged(ref _statusChangedAt, value);
    }

    public string CommentsTitle => ClashName;

    public string ClashName {
        get => _clashName;
        set => RaiseAndSetIfChanged(ref _clashName, value);
    }

    public string ImagePath => Clash?.ImagePath;

    public bool ClashDataIsValid {
        get => _clashDataIsValid;
        set => RaiseAndSetIfChanged(ref _clashDataIsValid, value);
    }

    /// <summary>Both object IDs were resolved in the active model or a loaded link.</summary>
    public bool IsResolved => GetFirstElement()?.GetElement(_revitRepository.DocInfos) is not null
        && GetSecondElement()?.GetElement(_revitRepository.DocInfos) is not null;

    public ClashCommentViewModel SelectedComment {
        get => _selectedComment;
        set => RaiseAndSetIfChanged(ref _selectedComment, value);
    }

    public bool CanEditComments {
        get => _canEditComments;
        set => RaiseAndSetIfChanged(ref _canEditComments, value);
    }

    public ObservableCollection<ClashCommentViewModel> Comments { get; }

    public ElementId FirstId { get; }

    public string FirstObjectName { get; }

    public string FirstTypeName { get; }

    public string FirstFamilyName { get; }

    public string FirstDocumentName { get; }

    public string FirstLevel { get; }

    public string FirstCategory { get; }

    public ElementId SecondId { get; }

    public string SecondObjectName { get; }

    public string SecondTypeName { get; }

    public string SecondFamilyName { get; }

    public string SecondLevel { get; }

    public string SecondDocumentName { get; }

    public string SecondCategory { get; }

    public ExpandoObject FirstElementParams { get; }

    public ExpandoObject SecondElementParams { get; }

    private ClashData ClashData { get; set; }


    /// <summary>
    /// Процент пересечения относительно объема первого элемента коллизии
    /// </summary>
    public double FirstElementIntersectionPercentage { get; private set; }

    /// <summary>
    /// Процент пересечения относительно объема второго элемента коллизии
    /// </summary>
    public double SecondElementIntersectionPercentage { get; private set; }

    /// <summary>
    /// Объем пересечения в м3
    /// </summary>
    public double IntersectionVolume { get; private set; }

    public ClashModel Clash { get; }

    public double FirstElementVolume { get; private set; }

    public double SecondElementVolume { get; private set; }


    public ClashModel GetClashModel() {
        Clash.ClashStatus = ClashStatus;
        Clash.Name = ClashName;
        Clash.Comments = Comments.Select(c => c.GetComment()).ToHashSet();
        return Clash;
    }

    /// <summary>
    /// Запоминает, что сервер принял текущий статус коллизии: с этого момента статус
    /// считается отправленным, а в колонках показываются автор и время изменения.
    /// </summary>
    /// <param name="changedBy">Учётная запись Windows автора изменения</param>
    /// <param name="changedAt">Момент изменения в UTC</param>
    public void MarkStatusSent(string changedBy, DateTime? changedAt) {
        Clash.ServerStatus = ServerClashStatus.ToServerStatus(ClashStatus);
        // Имя отправили вместе со статусом, поэтому показываем его не дожидаясь перезагрузки отчёта.
        Clash.StatusChangedByName = WindowsUser.FullName;
        SetStatusAuthor(changedBy, changedAt);
        RaisePropertyChanged(nameof(HasUnsentStatus));
    }

    private void SetStatusAuthor(string changedBy, DateTime? changedAt) {
        Clash.StatusChangedBy = changedBy;
        Clash.StatusChangedAt = changedAt;
        StatusChangedBy = changedBy ?? string.Empty;
        StatusChangedAt = changedAt is null
            ? string.Empty
            : changedAt.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
    }

    public ElementModel GetFirstElement() {
        return Clash.MainElement;
    }

    public ElementModel GetSecondElement() {
        return Clash.OtherElement;
    }

    public ICollection<ElementModel> GetElements() {
        return [GetFirstElement(), GetSecondElement()];
    }

    public override bool Equals(object obj) {
        return Equals(obj as ClashViewModel);
    }

    public override int GetHashCode() {
        int hashCode = 635569250;
        hashCode = hashCode * -1521134295 + ClashStatus.GetHashCode();
        hashCode = hashCode * -1521134295 + EqualityComparer<ElementId>.Default.GetHashCode(Clash.MainElement.Id);
        hashCode = hashCode * -1521134295 + EqualityComparer<ElementId>.Default.GetHashCode(Clash.OtherElement.Id);
        hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(FirstTypeName);
        hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(FirstDocumentName);
        hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(FirstLevel);
        hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(FirstCategory);
        hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(SecondTypeName);
        hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(SecondLevel);
        hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(SecondDocumentName);
        hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(SecondCategory);
        return hashCode;
    }

    public bool Equals(ClashViewModel other) {
        return other != null
            && Clash.MainElement.Id == other.Clash.MainElement.Id
            && Clash.OtherElement.Id == other.Clash.OtherElement.Id
            && FirstTypeName == other.FirstTypeName
            && FirstDocumentName == other.FirstDocumentName
            && FirstLevel == other.FirstLevel
            && FirstCategory == other.FirstCategory
            && SecondTypeName == other.SecondTypeName
            && SecondLevel == other.SecondLevel
            && SecondDocumentName == other.SecondDocumentName
            && SecondCategory == other.SecondCategory;
    }

    private void SetIntersectionData(ClashModel clashModel) {
        if(clashModel is null) {
            throw new ArgumentNullException(nameof(clashModel));
        }

        ClashData = clashModel
            .SetRevitRepository(_revitRepository)
            .GetClashData();
        ClashDataIsValid = ClashData.IsValid;

        FirstElementVolume = _revitRepository.ConvertToM3(ClashData.MainElementVolume);
        SecondElementVolume = _revitRepository.ConvertToM3(ClashData.OtherElementVolume);

        IntersectionVolume = Math.Round(_revitRepository.ConvertToM3(ClashData.ClashVolume), 6);
        FirstElementIntersectionPercentage = ClashData.MainElementVolume > 0
            ? Math.Round(ClashData.ClashVolume / ClashData.MainElementVolume * 100, 2)
            : 0;
        SecondElementIntersectionPercentage = ClashData.OtherElementVolume > 0
            ? Math.Round(ClashData.ClashVolume / ClashData.OtherElementVolume * 100, 2)
            : 0;
    }

    public void SetElementParams(string[] paramNames) {
        var firstElement = GetFirstElement().GetElement(_revitRepository.DocInfos);
        if(firstElement is not null) {
            SetElementParams(FirstElementParams, firstElement, paramNames);
        }
        var secondElement = GetSecondElement().GetElement(_revitRepository.DocInfos);
        if(secondElement is not null) {
            SetElementParams(SecondElementParams, secondElement, paramNames);
        }
    }

    public void ResetComments(IEnumerable<ClashCommentViewModel> comments) {
        Comments.Clear();
        foreach(var c in comments) {
            AddComment(c.GetComment());
        }
    }

    private void AddComment(ClashComment comment) {
        int id;
        if(Comments.Count == 0) {
            id = 1;
        } else {
            id = Comments.Max(c => c.Id) + 1;
        }

        comment.Id = id;
        var commentVm = new ClashCommentViewModel(_revitRepository, comment);
        Comments.Add(commentVm);
        SelectedComment = commentVm;
    }

    private void SetElementParams(IDictionary<string, object> elementParams, Element element, string[] paramNames) {
        elementParams.Clear();
        for(int i = 0; i < paramNames.Length; i++) {
            elementParams.Add($"{ElementParamFieldName}{i}", element.GetParamValueAsString(paramNames[i]));
        }
    }

    private void AddComment() {
        AddComment(new ClashComment() { Author = _revitRepository.UiApplication.Application.Username });
    }

    private void RemoveComment(ClashCommentViewModel comment) {
        Comments.Remove(comment);
        SelectedComment = Comments.FirstOrDefault();
    }

    private bool CanRemoveComment(ClashCommentViewModel comment) {
        return comment != null;
    }
}
