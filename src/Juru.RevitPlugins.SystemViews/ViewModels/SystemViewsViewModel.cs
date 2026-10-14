using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;

using Autodesk.Revit.DB;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Juru.RevitPlugins.SystemViews.Localization;
using Juru.RevitPlugins.SystemViews.Models;
using Juru.RevitPlugins.SystemViews.Services;

namespace Juru.RevitPlugins.SystemViews.ViewModels;

/// <summary>
///     Окно «Виды по системам»: какие категории, по какому параметру, какие системы и как оформить.
/// </summary>
public sealed partial class SystemViewsViewModel : ObservableObject
{
    private readonly IMepModelSource source;

    private bool reloadingParameters;

    /// <summary>
    ///     Системы, отмеченные в прошлый раз. Нужны только при первой загрузке списка: дальше
    ///     отметки ведёт сам пользователь.
    /// </summary>
    private HashSet<string>? savedSystems;

    [ObservableProperty]
    private SystemParameterInfo? selectedParameter;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string viewNamePrefix;

    [ObservableProperty]
    private TemplateOption selectedTemplate;

    [ObservableProperty]
    private bool fitSectionBox;

    public SystemViewsViewModel(IMepModelSource source, SystemViewsSettings settings)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));

        var chosen = settings.Categories?.ToHashSet(StringComparer.Ordinal);

        Categories = source.Categories
            .Select(info => new CategoryRowViewModel(
                info,
                DisciplineName(info.Discipline),
                chosen?.Contains(info.Category.ToString()) ?? MepCategories.IsDefault(info.Discipline),
                OnCategoryToggled))
            .ToList();

        Systems = [];
        SystemsView = CollectionViewSource.GetDefaultView(Systems);
        SystemsView.Filter = row => row is SystemRowViewModel system
                                    && (SearchText.Length is 0
                                        || system.Name.IndexOf(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0);

        Templates =
        [
            new TemplateOption(TemplateMode.AsSourceView, null, Text.TemplateAsSource),
            new TemplateOption(TemplateMode.None, null, Text.TemplateNone),
            .. source.Templates.Select(template => new TemplateOption(TemplateMode.Specific, template.Id, template.Name))
        ];

        selectedTemplate = Templates.FirstOrDefault(option => option.Mode == settings.TemplateMode
                                                              && (option.Mode is not TemplateMode.Specific || option.Name == settings.TemplateName))
                           ?? Templates[0];

        viewNamePrefix = settings.ViewNamePrefix;
        fitSectionBox = settings.FitSectionBox;
        savedSystems = (settings.Systems ?? []).ToHashSet(SystemNames.Comparer);

        ReloadParameters(settings.ParameterName);

        savedSystems = null;
    }

    /// <summary>Закрыть окно с выбранным: модель окна не знает о самом окне.</summary>
    public event EventHandler? Accepted;

    public string SourceViewName => source.SourceViewName;

    public IReadOnlyList<CategoryRowViewModel> Categories { get; }

    public string CategoriesSummary =>
        string.Format(Text.CategoriesSummary, Categories.Count(category => category.IsChecked), Categories.Count);

    public ObservableCollection<SystemParameterInfo> Parameters { get; } = [];

    /// <summary>Сколько выбранных категорий фильтруется параметром: остальные на схемах скрываются.</summary>
    public string ParameterNote =>
        SelectedParameter is null
            ? string.Empty
            : string.Format(Text.ParameterCategories, SelectedParameter.Name,
                SelectedParameter.Categories.Count, CheckedCategories().Count);

    public ObservableCollection<SystemRowViewModel> Systems { get; }

    public ICollectionView SystemsView { get; }

    public bool HasNoSystems => SelectedParameter is not null && Systems.Count is 0;

    public IReadOnlyList<TemplateOption> Templates { get; }

    public string Summary
    {
        get
        {
            var chosen = Systems.Where(system => system.IsChecked).ToList();
            var updating = chosen.Count(system => system.WillUpdate);
            var suspicious = chosen.Count(system => system.HasWarning);

            var summary = string.Format(Text.Summary, chosen.Count, chosen.Count - updating, updating);

            return suspicious is 0 ? summary : summary + string.Format(Text.SummarySuspicious, suspicious);
        }
    }

    /// <summary>Что сделать. Заполняется, когда пользователь нажал «Создать».</summary>
    public SystemViewsRequest? Request { get; private set; }

    /// <summary>Выбор этого запуска - для следующего.</summary>
    public SystemViewsSettings ToSettings()
    {
        return new SystemViewsSettings
        {
            ParameterName = SelectedParameter?.Name,
            Categories = CheckedCategories().Select(category => category.ToString()).ToList(),
            ViewNamePrefix = ViewNamePrefix,
            TemplateMode = SelectedTemplate.Mode,
            TemplateName = SelectedTemplate.Mode is TemplateMode.Specific ? SelectedTemplate.Name : null,
            FitSectionBox = FitSectionBox,
            Systems = Systems.Where(system => system.IsChecked).Select(system => system.Name).ToList()
        };
    }

    [RelayCommand]
    private void SelectAll()
    {
        SetVisible(true);
    }

    [RelayCommand]
    private void SelectNone()
    {
        SetVisible(false);
    }

    private bool CanCreate()
    {
        return SelectedParameter is not null
               && Systems.Any(system => system.IsChecked)
               && SystemNames.Sanitize(ViewNamePrefix ?? string.Empty).Length > 0;
    }

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private void Create()
    {
        Request = new SystemViewsRequest
        {
            Parameter = SelectedParameter!,
            Systems = Systems.Where(system => system.IsChecked).Select(system => system.Name).ToList(),
            Categories = CheckedCategories(),
            ViewNamePrefix = ViewNamePrefix,
            TemplateMode = SelectedTemplate.Mode,
            TemplateId = SelectedTemplate.Id,
            FitSectionBox = FitSectionBox
        };

        Accepted?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSelectedParameterChanged(SystemParameterInfo? value)
    {
        // Пока список параметров пересобирается, ComboBox сбрасывает выбор в null и ставит заново:
        // перечитывать системы на каждом шаге незачем, их перечитает сама пересборка.
        if (reloadingParameters)
        {
            return;
        }

        ReloadSystems(CheckedSystemNames());
        OnPropertyChanged(nameof(ParameterNote));
    }

    partial void OnSearchTextChanged(string value)
    {
        SystemsView.Refresh();
    }

    partial void OnViewNamePrefixChanged(string value)
    {
        foreach (var system in Systems)
        {
            system.WillUpdate = WillUpdate(system.Name);
        }

        OnPropertyChanged(nameof(Summary));
    }

    private void OnCategoryToggled()
    {
        OnPropertyChanged(nameof(CategoriesSummary));
        ReloadParameters(SelectedParameter?.Name);
    }

    /// <summary>
    ///     Список параметров зависит от категорий. Выбранный параметр сохраняется, если он ещё есть;
    ///     иначе берётся «Имя системы» - встроенный параметр, который есть у всех инженерных категорий.
    /// </summary>
    private void ReloadParameters(string? preferredName)
    {
        var keep = CheckedSystemNames();

        reloadingParameters = true;

        try
        {
            Parameters.Clear();

            foreach (var parameter in source.FindParameters(CheckedCategories()))
            {
                Parameters.Add(parameter);
            }

            var systemName = (long) BuiltInParameter.RBS_SYSTEM_NAME_PARAM;

            SelectedParameter = Parameters.FirstOrDefault(parameter => parameter.Name == preferredName)
                                ?? Parameters.FirstOrDefault(parameter => parameter.Id == systemName)
                                ?? Parameters.FirstOrDefault();
        }
        finally
        {
            reloadingParameters = false;
        }

        ReloadSystems(keep);
        OnPropertyChanged(nameof(ParameterNote));
    }

    private HashSet<string> CheckedSystemNames()
    {
        return Systems.Where(system => system.IsChecked).Select(system => system.Name).ToHashSet(SystemNames.Comparer);
    }

    /// <summary>
    ///     Перечитывает системы; отмеченные раньше остаются отмеченными, если они ещё есть. При
    ///     открытии окна отмечены системы прошлого запуска и те, у которых схема уже есть.
    /// </summary>
    private void ReloadSystems(HashSet<string> previouslyChecked)
    {
        Systems.Clear();

        if (SelectedParameter is not null)
        {
            foreach (var system in source.FindSystems(SelectedParameter))
            {
                var willUpdate = WillUpdate(system.Name);
                var isChecked = previouslyChecked.Contains(system.Name)
                                || (savedSystems is not null && (savedSystems.Contains(system.Name) || willUpdate));

                Systems.Add(new SystemRowViewModel(system, isChecked, willUpdate, OnSystemToggled));
            }
        }

        OnPropertyChanged(nameof(HasNoSystems));
        OnSystemToggled();
    }

    private void OnSystemToggled()
    {
        OnPropertyChanged(nameof(Summary));
        CreateCommand.NotifyCanExecuteChanged();
    }

    private void SetVisible(bool isChecked)
    {
        foreach (var system in SystemsView.Cast<SystemRowViewModel>())
        {
            // Почти пустые схемы «Выбрать все» не берёт: их отмечают вручную, если они правда нужны.
            system.IsChecked = isChecked && !system.HasWarning;
        }
    }

    private bool WillUpdate(string system)
    {
        return source.ExistingViewNames.Contains(SystemNames.ViewName(ViewNamePrefix ?? string.Empty, system));
    }

    private IReadOnlyList<BuiltInCategory> CheckedCategories()
    {
        return Categories.Where(category => category.IsChecked).Select(category => category.Category).ToList();
    }

    private static string DisciplineName(MepDiscipline discipline)
    {
        return discipline switch
        {
            MepDiscipline.Hvac => Text.Hvac,
            MepDiscipline.Plumbing => Text.Plumbing,
            _ => Text.Electrical
        };
    }
}

/// <summary>Строка категории.</summary>
public sealed partial class CategoryRowViewModel : ObservableObject
{
    private readonly Action toggled;

    [ObservableProperty]
    private bool isChecked;

    public CategoryRowViewModel(MepCategoryInfo info, string discipline, bool isChecked, Action toggled)
    {
        Info = info;
        Discipline = discipline;
        this.isChecked = isChecked;
        this.toggled = toggled;
    }

    public MepCategoryInfo Info { get; }

    public BuiltInCategory Category => Info.Category;

    public string Name => Info.Name;

    public string Discipline { get; }

    public string Count => string.Format(Text.ElementCount, Info.ElementCount);

    /// <summary>Категории без элементов на виде видны, но приглушены: схемам они ничего не добавят.</summary>
    public bool HasElements => Info.ElementCount > 0;

    partial void OnIsCheckedChanged(bool value)
    {
        toggled();
    }
}

/// <summary>Строка системы.</summary>
public sealed partial class SystemRowViewModel : ObservableObject
{
    private readonly Action toggled;

    [ObservableProperty]
    private bool isChecked;

    [ObservableProperty]
    private bool willUpdate;

    public SystemRowViewModel(SystemInfo info, bool isChecked, bool willUpdate, Action toggled)
    {
        Name = info.Name;
        ElementCount = info.ElementCount;
        Count = string.Format(Text.ElementCount, info.ElementCount);
        HasWarning = info.IsFragment;
        this.isChecked = isChecked;
        this.willUpdate = willUpdate;
        this.toggled = toggled;
    }

    public string Name { get; }

    public int ElementCount { get; }

    public string Count { get; }

    /// <summary>Почти пустая схема: похоже на обрывок системы.</summary>
    public bool HasWarning { get; }

    public string? Warning => HasWarning ? Text.WarningFew : null;

    public string? WarningTip => HasWarning ? string.Format(Text.WarningTip, ElementCount) : null;

    public string WillUpdateText => Text.WillUpdate;

    partial void OnIsCheckedChanged(bool value)
    {
        toggled();
    }
}

/// <summary>Вариант шаблона в выпадающем списке.</summary>
public sealed record TemplateOption(TemplateMode Mode, long? Id, string Name);
