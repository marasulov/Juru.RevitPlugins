using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

using dosymep.Revit.Geometry;
using dosymep.SimpleServices;
using dosymep.WPF.Commands;
using dosymep.WPF.ViewModels;

using Juru.RevitPlugins.ClashNavigator.Models;
using Juru.RevitPlugins.ClashNavigator.Models.Clashes;
using Juru.RevitPlugins.ClashNavigator.Models.FilterModel;
using Juru.RevitPlugins.ClashNavigator.Models.Interfaces;
using Juru.RevitPlugins.ClashNavigator.Services.ClashStatuses;
using Juru.RevitPlugins.ClashNavigator.ViewModels.ClashDetective;
using Juru.RevitPlugins.ClashNavigator.ViewModels.Services;

using Wpf.Ui;

namespace Juru.RevitPlugins.ClashNavigator.ViewModels.Navigator;
internal class ReportViewModel : BaseViewModel, INamedEntity, IEquatable<ReportViewModel> {

    private RevitRepository _revitRepository;

    private string _name;
    private string _message;
    private DispatcherTimer _timer;
    private List<ClashViewModel> _allClashes;
    private ObservableCollection<ClashViewModel> _clashes;
    private IClashViewModel _selectedGuiClash;
    private ObservableCollection<IClashViewModel> _guiClashes;
    private ObservableCollection<IClashViewModel> _selectedGuiClashes = [];
    private double _firstIntersectionPercentage;
    private double _secondIntersectionPercentage;
    private bool _showImaginaryClashes;
    private ImaginaryFirstClashViewModel[] _imaginaryFirstClashes;
    private ImaginarySecondClashViewModel[] _imaginarySecondClashes;
    private readonly ILocalizationService _localization;
    private readonly IContentDialogService _contentDialogService;
    private readonly SettingsConfig _settingsConfig;
    private readonly IClashStatusService _clashStatusService;

    private static readonly string[] _filterableColumns = [
        nameof(IClashViewModel.ClashName),
        nameof(IClashViewModel.FirstDocumentName),
        nameof(IClashViewModel.FirstId),
        nameof(IClashViewModel.FirstObjectName),
        nameof(IClashViewModel.SecondDocumentName),
        nameof(IClashViewModel.SecondId),
        nameof(IClashViewModel.SecondObjectName),
        nameof(IClashViewModel.ClashStatusName),
        nameof(IClashViewModel.StatusAuthorInfo),
        nameof(IClashViewModel.StatusChangedAt)
    ];

    /// <summary>
    /// Колонки, значения которых зависят от статуса коллизии: их списки значений в фильтрах
    /// приходится обновлять после каждой правки статуса, иначе фильтр начинает скрывать строки
    /// с новым, ещё не известным ему значением.
    /// </summary>
    private static readonly string[] _statusDependentColumns = [
        nameof(IClashViewModel.ClashStatusName),
        nameof(IClashViewModel.StatusAuthorInfo),
        nameof(IClashViewModel.StatusChangedAt)
    ];

    public ReportViewModel(RevitRepository revitRepository,
        string name,
        ILocalizationService localization,
        IOpenFileDialogService openFileDialogService,
        ISaveFileDialogService saveFileDialogService,
        IMessageBoxService messageBoxService,
        IContentDialogService contentDialogService,
        SettingsConfig settingsConfig,
        IClashStatusService clashStatusService,
        ICollection<ClashModel> clashes) {

        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        OpenFileDialogService = openFileDialogService ?? throw new ArgumentNullException(nameof(openFileDialogService));
        SaveFileDialogService = saveFileDialogService ?? throw new ArgumentNullException(nameof(saveFileDialogService));
        MessageBoxService = messageBoxService ?? throw new ArgumentNullException(nameof(messageBoxService));
        _contentDialogService = contentDialogService ?? throw new ArgumentNullException(nameof(contentDialogService));
        _settingsConfig = settingsConfig ?? throw new ArgumentNullException(nameof(settingsConfig));
        _clashStatusService = clashStatusService ?? throw new ArgumentNullException(nameof(clashStatusService));

        // "Нет пересечения" пользователь не назначает: этот статус выставляет пересчёт геометрии,
        // а <see cref="ClashViewModel.ClashStatus"/> его присвоение и не примет.
        ClashStatuses = new ReadOnlyCollection<ClashStatusViewModel>([
            new ClashStatusViewModel(_localization, ClashStatus.Active),
            new ClashStatusViewModel(_localization, ClashStatus.Analized),
            new ClashStatusViewModel(_localization, ClashStatus.Solved),
        ]);

        ColumnFilters = _filterableColumns.ToDictionary(
            p => p,
            p => new ColumnFilterViewModel(p, ApplyColumnFilters, _localization));

        Initialize(revitRepository, name);
        if(clashes is null) {
            throw new ArgumentNullException(nameof(clashes));
        }

        InitializeClashes(clashes);
    }


    public IReadOnlyCollection<ClashStatusViewModel> ClashStatuses { get; }

    /// <summary>
    /// Фильтры по значениям колонок таблицы коллизий, ключ - имя свойства <see cref="IClashViewModel"/>.
    /// </summary>
    public IReadOnlyDictionary<string, ColumnFilterViewModel> ColumnFilters { get; }

    public string Name {
        get => _name;
        set => RaiseAndSetIfChanged(ref _name, value);
    }

    public string Message {
        get => _message;
        set => RaiseAndSetIfChanged(ref _message, value);
    }

    public IClashViewModel SelectedGuiClash {
        get => _selectedGuiClash;
        set {
            if(!(_selectedGuiClash?.Equals(value) ?? false)) {
                if(_selectedGuiClash is not null) {
                    _selectedGuiClash.PropertyChanged -= OnSelectedGuiClashChanged;
                }
                RaiseAndSetIfChanged(ref _selectedGuiClash, value);
                if(_selectedGuiClash is not null) {
                    _selectedGuiClash.PropertyChanged += OnSelectedGuiClashChanged;
                }
            }
        }
    }

    public ObservableCollection<IClashViewModel> GuiClashes {
        get => _guiClashes;
        private set => RaiseAndSetIfChanged(ref _guiClashes, value);
    }

    public int ImportedClashesCount => _allClashes?.Count ?? 0;

    /// <summary>
    /// Счётчик импортированных коллизий строкой для окна.
    /// </summary>
    /// <remarks>
    /// Собирается здесь, а не в разметке: там подстановка идёт через <c>StringFormat</c>,
    /// куда ключ словаря не подставить.
    /// </remarks>
    public string ImportedClashesInfo =>
        _localization.GetLocalizedString("Navigator.ImportedClashes", ImportedClashesCount);

    /// <inheritdoc cref="ImportedClashesInfo" />
    public string ResolvedClashesInfo =>
        _localization.GetLocalizedString("Navigator.ResolvedClashes", ResolvedClashesCount);

    /// <summary>
    /// Сколько коллизий проходит текущий фильтр колонок, то есть видно в таблице.
    /// </summary>
    public int VisibleClashesCount { get; private set; }

    /// <summary>
    /// Хотя бы одна колонка отфильтрована.
    /// </summary>
    public bool HasActiveFilters => ColumnFilters.Values.Any(filter => filter.IsActive);

    /// <summary>
    /// Сообщение о действующем фильтре. Пустая строка, если фильтров нет: тогда счётчик
    /// совпадает с количеством импортированных коллизий и только мешал бы.
    /// </summary>
    public string FilterInfo => HasActiveFilters
        ? _localization.GetLocalizedString(
            "Navigator.FilterInfo", VisibleClashesCount, GuiClashes?.Count ?? 0)
        : string.Empty;

    /// <summary>
    /// Количество коллизий, статус которых изменён, но ещё не отправлен на сервер.
    /// </summary>
    public int UnsentStatusesCount => _allClashes?.Count(c => c.HasUnsentStatus) ?? 0;

    /// <summary>
    /// Отчёт загружен с сервера коллизий: у коллизий известен их Id в базе,
    /// а значит статусы можно отправлять обратно.
    /// </summary>
    public bool IsFromServer => _allClashes?.Any(c => c.Clash.ServerId is not null) ?? false;

    /// <summary>
    /// Предупреждение о том, что статусы этого отчёта в базу не уйдут. Пустая строка у отчёта с сервера.
    /// Показывается постоянно: пользователь должен видеть это до того, как начнёт менять статусы.
    /// </summary>
    /// <summary>
    /// Предупреждение о том, что ни один элемент отчёта не найден в открытом документе, —
    /// тогда кнопки «Показать» неактивны, и без объяснения это выглядит поломкой.
    /// </summary>
    public string UnresolvedInfo => ImportedClashesCount > 0 && ResolvedClashesCount == 0
        ? _localization.GetLocalizedString("Navigator.UnresolvedInfo", GetReportDocumentNames())
        : string.Empty;

    public string ServerLinkInfo => IsFromServer
        ? string.Empty
        : _localization.GetLocalizedString("Navigator.NoServerLink");

    /// <summary>
    /// Подсказка о неотправленных изменениях статуса. Пустая строка, если всё отправлено.
    /// </summary>
    public string UnsentStatusesMessage => UnsentStatusesCount == 0
        ? string.Empty
        : _localization.GetLocalizedString("Navigator.UnsentStatuses", UnsentStatusesCount);

    public int ResolvedClashesCount => _allClashes?.Count(c => c.IsResolved) ?? 0;

    public ObservableCollection<IClashViewModel> SelectedGuiClashes {
        get => _selectedGuiClashes;
        set => RaiseAndSetIfChanged(ref _selectedGuiClashes, value);
    }

    public double FirstIntersectionPercentage {
        get => _firstIntersectionPercentage;
        private set => RaiseAndSetIfChanged(ref _firstIntersectionPercentage, value);
    }

    public double SecondIntersectionPercentage {
        get => _secondIntersectionPercentage;
        private set => RaiseAndSetIfChanged(ref _secondIntersectionPercentage, value);
    }

    public bool ShowImaginaryClashes {
        get => _showImaginaryClashes;
        set => RaiseAndSetIfChanged(ref _showImaginaryClashes, value);
    }

    public ICommand SaveCommand { get; private set; }

    public ICommand SaveAsCommand { get; private set; }

    public ICommand ShowImaginaryClashesChangedCommand { get; private set; }

    public ICommand ResetFiltersCommand { get; private set; }

    public ICommand PickFirstElementsCommand { get; private set; }

    public ICommand PickSecondElementsCommand { get; private set; }

    public ICommand PickBothElementsCommand { get; private set; }

    public IOpenFileDialogService OpenFileDialogService { get; }

    public ISaveFileDialogService SaveFileDialogService { get; }

    public IMessageBoxService MessageBoxService { get; }

    private ObservableCollection<ClashViewModel> Clashes {
        get => _clashes;
        set => RaiseAndSetIfChanged(ref _clashes, value);
    }

    public ClashesConfig GetUpdatedConfig() {
        var config = ClashesConfig.GetClashesConfig(_revitRepository.GetObjectName(), Name);

        var notValidClashes = _allClashes.Except(Clashes)
                                         .Select(item => item.GetClashModel());

        config.Clashes = Clashes.Select(item => item.GetClashModel())
            .Union(notValidClashes)
            .ToList();

        return config;
    }

    public override bool Equals(object obj) {
        return Equals(obj as ReportViewModel);
    }

    public override int GetHashCode() {
        int hashCode = 1681366416;
        hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(Name);
        return hashCode;
    }

    public bool Equals(ReportViewModel other) {
        return other != null
            && Name == other.Name;
    }

    public ICollection<ClashViewModel> GetClashes() {
        return _allClashes ?? [];
    }

    public void ResetClashes(IEnumerable<ClashViewModel> clashes) {
        ShowImaginaryClashes = false;
        _allClashes = clashes.ToList();
        Clashes = new ObservableCollection<ClashViewModel>(_allClashes);

        GuiClashes = new ObservableCollection<IClashViewModel>(Clashes.OrderBy(c => c.ClashName));
        SetIntersectionPercentage(GuiClashes);
        RaisePropertyChanged(nameof(ImportedClashesCount));
        RaisePropertyChanged(nameof(ImportedClashesInfo));
        RaisePropertyChanged(nameof(ResolvedClashesCount));
        RaisePropertyChanged(nameof(ResolvedClashesInfo));
        RaisePropertyChanged(nameof(UnsentStatusesCount));
        RaisePropertyChanged(nameof(UnsentStatusesMessage));
        RaisePropertyChanged(nameof(IsFromServer));
        RaisePropertyChanged(nameof(ServerLinkInfo));
        RaisePropertyChanged(nameof(UnresolvedInfo));
        RefreshColumnFilters();
    }

    private void Initialize(RevitRepository revitRepository, string name) {
        _revitRepository = revitRepository ?? throw new ArgumentNullException(nameof(revitRepository));
        Name = name ?? throw new ArgumentNullException(nameof(name));

        InitializeTimer();

        SaveCommand = RelayCommand.Create(Save);
        SaveAsCommand = RelayCommand.Create(SaveAs);
        ShowImaginaryClashesChangedCommand = RelayCommand.Create(ShowImaginaryClashesChanged);
        ResetFiltersCommand = RelayCommand.Create(ResetFilters, CanResetFilters);
        PickFirstElementsCommand = RelayCommand.Create(
            PickFirstElements, CanPickElements);
        PickSecondElementsCommand = RelayCommand.Create(
            PickSecondElements, CanPickElements);
        PickBothElementsCommand = RelayCommand.Create(
            PickBothElements, CanPickElements);
    }

    /// <summary>
    /// Отправляет в базу статусы, отличающиеся от известных серверу.
    /// Вызывается только кнопкой "Отправить статусы": сохранение отчёта пишет файлы и в базу не лезет,
    /// иначе две кнопки делали бы одно и то же.
    /// </summary>
    /// <returns>Текст результата (null, если отправлять было нечего) и признак ошибки.</returns>
    public (string? Message, bool IsError) SendStatuses() {
        var result = SendStatusesToServer();
        if(result.Message is not null) {
            // Сообщения об отправке не гасим таймером: пользователь должен успеть их прочитать.
            Message = result.Message;
        }
        return result;
    }

    /// <summary>
    /// Записывает отчёт в файл профиля. В базу ничего не отправляет: это дело кнопки
    /// "Отправить статусы". Неотправленные статусы сохраняются в файле и остаются в очереди.
    /// </summary>
    private void Save() {
        GetUpdatedConfig().SaveProjectConfig();

        Message = UnsentStatusesCount == 0
            ? _localization.GetLocalizedString("Navigator.ReportSaved")
            : _localization.GetLocalizedString("Navigator.ReportSavedUnsent", UnsentStatusesCount);
        RefreshMessage();
    }

    private void SaveAs() {
        var config = GetUpdatedConfig();
        var saver = new ConfigSaverService(_revitRepository, SaveFileDialogService);
        saver.Save(config);
        Message = _localization.GetLocalizedString("Navigator.ReportSaved");
        RefreshMessage();
    }

    private void InitializeClashes(IEnumerable<ClashModel> clashModels) {
        ResetClashes(clashModels.Select(item => new ClashViewModel(
            _revitRepository, item, _localization, ClashStatuses, OnClashStatusChanged)));
    }

    private void SetIntersectionPercentage(ICollection<IClashViewModel> clashes) {
        double firstTotalVolume = clashes
            .Select(GetFirstElementViewModel)
            .Where(v => v is not null)
            .Distinct()
            .Sum(e => e.ElementVolume);
        double secondTotalVolume = clashes
            .Select(GetSecondElementViewModel)
            .Where(v => v is not null)
            .Distinct()
            .Sum(e => e.ElementVolume);
        double collisionTotalVolume = clashes.Select(c => c.IntersectionVolume).Sum();

        FirstIntersectionPercentage = Math.Round(collisionTotalVolume / firstTotalVolume * 100, 2);
        SecondIntersectionPercentage = Math.Round(collisionTotalVolume / secondTotalVolume * 100, 2);
    }

    private ElementViewModel GetFirstElementViewModel(IClashViewModel clash) {
        try {
            return new ElementViewModel(clash.GetFirstElement(), clash.FirstElementVolume);
        } catch(NotSupportedException) {
            return null;
        }
    }

    private ElementViewModel GetSecondElementViewModel(IClashViewModel clash) {
        try {
            return new ElementViewModel(clash.GetSecondElement(), clash.SecondElementVolume);
        } catch(NotSupportedException) {
            return null;
        }
    }

    private void ShowImaginaryClashesChanged() {
        if(ShowImaginaryClashes) {
            FindImaginaryClashes();
            GuiClashes = new ObservableCollection<IClashViewModel>(
                [.. Clashes, .. _imaginaryFirstClashes, .. _imaginarySecondClashes]);
        } else {
            GuiClashes = new ObservableCollection<IClashViewModel>(Clashes);
        }
        SetIntersectionPercentage(GuiClashes);
        RefreshColumnFilters();
    }

    /// <summary>
    /// Обновляет доступные значения фильтров по актуальному содержимому <see cref="GuiClashes"/>
    /// и переустанавливает фильтр на представление коллекции.
    /// </summary>
    private void RefreshColumnFilters() {
        foreach(var filter in ColumnFilters.Values) {
            var property = typeof(IClashViewModel).GetProperty(filter.PropertyName);
            var values = GuiClashes.Select(c => GetPropertyStringValue(property, c));
            filter.SetOptions(values);
        }
        ApplyColumnFilters();
    }

    private void ApplyColumnFilters() {
        var view = CollectionViewSource.GetDefaultView(GuiClashes);
        if(view is null) {
            return;
        }

        var activeFilters = ColumnFilters.Values.Where(f => f.IsActive).ToArray();
        var properties = activeFilters.ToDictionary(
            f => f, f => typeof(IClashViewModel).GetProperty(f.PropertyName));

        view.Filter = activeFilters.Length == 0
            ? null
            : item => item is IClashViewModel clash
                && activeFilters.All(f => f.Matches(GetPropertyStringValue(properties[f], clash)));

        // Считаем по представлению, а не по коллекции: именно оно знает, что осталось после фильтра.
        VisibleClashesCount = view.Cast<object>().Count();
        RaisePropertyChanged(nameof(VisibleClashesCount));
        RaisePropertyChanged(nameof(HasActiveFilters));
        RaisePropertyChanged(nameof(FilterInfo));
    }

    private void ResetFilters() {
        foreach(var filter in ColumnFilters.Values) {
            filter.Reset();
        }
        ApplyColumnFilters();
    }

    private bool CanResetFilters() {
        return HasActiveFilters;
    }

    private static string GetPropertyStringValue(PropertyInfo property, IClashViewModel clash) {
        return property?.GetValue(clash)?.ToString() ?? string.Empty;
    }

    private CheckViewModel GetCheckViewModel() {
        string path = Path.Combine(_revitRepository.GetObjectName(), _revitRepository.GetDocumentName());
        var checks = ChecksConfig.GetChecksConfig(path, _revitRepository.Doc);
        var filters = FiltersConfig.GetFiltersConfig(path, _revitRepository.Doc);

        return checks.Checks
            .Select(c => new CheckViewModel(_revitRepository,
            _localization,
            OpenFileDialogService,
            SaveFileDialogService,
            MessageBoxService,
            _settingsConfig,
            filters,
            _contentDialogService,
            c))
            .FirstOrDefault(c => c.ReportName.Equals(Name));
    }

    private void FindImaginaryClashes() {
        if(_imaginaryFirstClashes is null || _imaginarySecondClashes is null) {
            var checkViewModel = GetCheckViewModel();
            if(checkViewModel is not null) {
                (var firstProviders, var secondProviders) = checkViewModel.GetProviders();
                _imaginaryFirstClashes = FilterElements(Clashes, GetElementViewModels(firstProviders))
                    .Select(e => new ImaginaryFirstClashViewModel(_revitRepository, e, _localization))
                    .ToArray();
                _imaginarySecondClashes = FilterElements(Clashes, GetElementViewModels(secondProviders))
                    .Select(e => new ImaginarySecondClashViewModel(_revitRepository, e, _localization))
                    .ToArray();
            } else {
                _imaginaryFirstClashes = [];
                _imaginarySecondClashes = [];
            }
        }
    }

    /// <summary>
    /// Находит элементы, которые не участвуют в коллизиях
    /// </summary>
    /// <param name="clashes">Коллизии</param>
    /// <param name="elementsToFilter">Элементы для проверки</param>
    /// <returns>Элементы из заданной коллекции, которые не участвуют в заданных коллизиях</returns>
    private ICollection<ElementViewModel> FilterElements(
        ICollection<ClashViewModel> clashes,
        ICollection<ElementViewModel> elementsToFilter) {

        var clashElements = clashes.SelectMany(c => new[] { c.Clash.MainElement, c.Clash.OtherElement })
            .ToHashSet();

        return [.. elementsToFilter.Where(e => !clashElements.Contains(e.Element))];
    }

    private ICollection<ElementViewModel> GetElementViewModels(ICollection<IProvider> providers) {
        return [.. providers.SelectMany(GetElementViewModels)];
    }

    private ICollection<ElementViewModel> GetElementViewModels(IProvider provider) {
        return [.. provider.GetElements()
            .Select(e => new ElementViewModel(
                new ElementModel(e, provider.MainTransform),
                _revitRepository.ConvertToM3(e.GetSolids().Sum(s => s.GetVolumeOrDefault(0) ?? 0))))];
    }

    private void InitializeTimer() {
        _timer = new DispatcherTimer {
            Interval = new TimeSpan(0, 0, 0, 3)
        };
        _timer.Tick += (s, a) => { Message = null; _timer.Stop(); };
    }

    private void RefreshMessage() {
        _timer.Start();
    }

    private void OnSelectedGuiClashChanged(object sender, PropertyChangedEventArgs e) {
        if(string.IsNullOrWhiteSpace(e.PropertyName)) {
            return;
        }
        var prop = typeof(IClashViewModel).GetProperty(e.PropertyName);
        if(prop is null) {
            return;
        }
        if(prop.Name == nameof(IClashViewModel.ClashStatus)) {
            // мультиредактирование только для статуса коллизии
            object newValue = prop.GetValue(SelectedGuiClash);

            IClashViewModel[] sheets = [.. SelectedGuiClashes];
            foreach(var item in sheets) {
                if(!item.Equals(SelectedGuiClash)) {
                    prop.SetValue(item, newValue);
                }
            }

            RaiseUnsentStatusesChanged();
        }
    }

    /// <summary>
    /// Отправляет в базу статусы, отличающиеся от известных серверу, и запоминает автора изменения.
    /// Отправленными помечаются только принятые сервером коллизии, поэтому следующая отправка
    /// повторит запрос по неудавшимся.
    /// </summary>
    /// <returns>Текст для строки состояния (null, если отправлять было нечего) и признак ошибки.</returns>
    private (string? Message, bool IsError) SendStatusesToServer() {
        // Коллизия попадает сюда только если её Id в базе известен: см. ClashViewModel.HasUnsentStatus.
        var sendable = GetUnsentClashes();
        if(sendable.Length == 0) {
            return (null, false);
        }

        var result = _clashStatusService.UpdateStatuses(
            sendable.Select(c => c.GetClashModel()).ToArray());
        if(!result.RequestDelivered) {
            return (_localization.GetLocalizedString(
                "Navigator.StatusesNotSent", result.ErrorMessage), true);
        }

        var accepted = sendable.Where(c => !result.FailedClashIds.Contains(c.Clash.ServerId!.Value));
        foreach(var clash in accepted) {
            clash.MarkStatusSent(result.ChangedBy, result.ChangedAt);
        }
        RaiseUnsentStatusesChanged();

        if(result.HasError) {
            // Часть статусов записана: повторная отправка уйдёт только по непринятым коллизиям.
            return (_localization.GetLocalizedString(
                "Navigator.StatusesSentPartly", result.Updated, result.ErrorMessage), true);
        }

        return (_localization.GetLocalizedString(
            "Navigator.StatusesSent", result.Updated, result.ChangedBy), false);
    }

    private ClashViewModel[] GetUnsentClashes() {
        return [.. GetClashes().Where(c => c.HasUnsentStatus)];
    }

    /// <summary>
    /// Реакция на правку статуса пользователем: пересчитывает счётчик неотправленных
    /// и обновляет значения фильтров по колонкам, зависящим от статуса.
    /// </summary>
    private void OnClashStatusChanged() {
        RaiseUnsentStatusesChanged();
        if(GuiClashes is null) {
            return;
        }

        var filters = _statusDependentColumns
            .Where(ColumnFilters.ContainsKey)
            .Select(name => ColumnFilters[name])
            .ToArray();
        foreach(var filter in filters) {
            var property = typeof(IClashViewModel).GetProperty(filter.PropertyName);
            filter.SetOptions(GuiClashes.Select(clash => GetPropertyStringValue(property, clash)));
        }
        ApplyColumnFilters();
    }

    /// <summary>
    /// Файлы, к которым относятся коллизии отчёта. Больше трёх не показываем: строка состояния
    /// не должна разрастаться на весь список связей.
    /// </summary>
    private string GetReportDocumentNames() {
        var names = (_allClashes ?? [])
            .SelectMany(clash => new[] { clash.FirstDocumentName, clash.SecondDocumentName })
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct()
            .Take(3)
            .ToArray();
        return names.Length == 0
            ? _localization.GetLocalizedString("Navigator.NoDocumentName")
            : string.Join(", ", names);
    }

    private void RaiseUnsentStatusesChanged() {
        RaisePropertyChanged(nameof(UnsentStatusesCount));
        RaisePropertyChanged(nameof(UnsentStatusesMessage));
    }

    private void PickFirstElements() {
        List<ElementModel> elements = [];
        foreach(var clash in SelectedGuiClashes) {
            try {
                elements.Add(clash.GetFirstElement());
            } catch(NotSupportedException) {
                continue;
            }
        }
        _revitRepository.SelectElements(elements);
    }

    private void PickSecondElements() {
        List<ElementModel> elements = [];
        foreach(var clash in SelectedGuiClashes) {
            try {
                elements.Add(clash.GetSecondElement());
            } catch(NotSupportedException) {
                continue;
            }
        }
        _revitRepository.SelectElements(elements);
    }

    private void PickBothElements() {
        List<ElementModel> elements = [];
        foreach(var clash in SelectedGuiClashes) {
            elements.AddRange(clash.GetElements());
        }
        _revitRepository.SelectElements(elements);
    }

    private bool CanPickElements() {
        return SelectedGuiClashes?.Count > 0;
    }
}
