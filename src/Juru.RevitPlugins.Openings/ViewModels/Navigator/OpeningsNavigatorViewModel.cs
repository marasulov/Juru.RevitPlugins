using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
// Псевдонимы: в родительском пространстве имён лежит свой RelayCommand, оставшийся от старых окон.
using ShowTaskCommand = CommunityToolkit.Mvvm.Input.RelayCommand<
    Juru.RevitPlugins.Openings.ViewModels.Navigator.OpeningTaskViewModel>;
using ToolkitCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

using Juru.RevitPlugins.Openings.Models.Configs;
using Juru.RevitPlugins.Openings.Models.Intersections;
using Juru.RevitPlugins.Openings.Models.Navigator;
using Juru.RevitPlugins.Openings.Services;

namespace Juru.RevitPlugins.Openings.ViewModels.Navigator;

/// <summary>
/// Навигатор по заданиям на отверстия, выданным из активного файла.
/// </summary>
public partial class OpeningsNavigatorViewModel : ObservableObject {
    private readonly OpeningsConfig _config;
    private readonly RevitActionRunner _runner;

    [ObservableProperty]
    private OpeningTaskViewModel? _selectedTask;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private string _linksText = string.Empty;

    public OpeningsNavigatorViewModel(OpeningsConfig config, RevitActionRunner runner) {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));

        Tasks = [];
        ShowCommand = new ShowTaskCommand(Show, task => task is not null);
        RefreshCommand = new ToolkitCommand(Refresh);
    }

    /// <summary>
    /// Задания активного файла.
    /// </summary>
    public ObservableCollection<OpeningTaskViewModel> Tasks { get; }

    /// <summary>
    /// Название набора настроек, по которым назначены состояния.
    /// </summary>
    public string ConfigName => _config.Name;

    /// <summary>
    /// Выделяет задание в модели и показывает его на виде.
    /// </summary>
    public IRelayCommand<OpeningTaskViewModel> ShowCommand { get; }

    /// <summary>
    /// Пересчитывает состояния заново.
    /// </summary>
    public IRelayCommand RefreshCommand { get; }

    /// <summary>
    /// Перечитывает задания и пересчитывает их состояния. Вызывается только в контексте Revit API.
    /// </summary>
    public void Reload(UIApplication uiApplication) {
        if(uiApplication is null) { throw new ArgumentNullException(nameof(uiApplication)); }

        var doc = uiApplication.ActiveUIDocument.Document;
        var tasks = new OpeningTasksProvider(doc, _config).GetTasks();
        var links = new StructureLinksProvider(doc, _config).GetProviders();

        LinksText = links.Count == 0
            ? "Связей с конструкциями не найдено: состояния определены только по инженерным элементам."
            : "Связи с конструкциями: "
                + string.Join(", ", links.Select(link => $"{link.LinkName} ({link.Discipline})"));

        var updated = UpdateStatuses(uiApplication, doc, tasks, links);

        Tasks.Clear();
        foreach(var task in updated) {
            Tasks.Add(new OpeningTaskViewModel(task));
        }

        Summary = GetSummary(Tasks);
        SelectedTask = Tasks.FirstOrDefault();
    }

    private IReadOnlyCollection<OpeningTask> UpdateStatuses(
        UIApplication uiApplication,
        Document doc,
        IReadOnlyCollection<OpeningTask> tasks,
        IReadOnlyCollection<StructureElementsProvider> links) {

        if(tasks.Count == 0) {
            return tasks;
        }

        var mepElements = new MepElementsIndex(doc, _config);
        var updater = new OpeningTaskStatusUpdater(doc, mepElements, links, tasks);

        using var progress = new OperationProgress(uiApplication.MainWindowHandle);
        progress.StartStage("Проверка заданий", tasks.Count);

        List<OpeningTask> processed = [];
        int done = 0;
        foreach(var task in tasks) {
            if(progress.Token.IsCancellationRequested) {
                // Отменённая проверка не оставляет список пустым: показываем то, что успели посчитать.
                break;
            }

            updater.Update(task);
            processed.Add(task);
            progress.Report(++done);
        }

        return processed;
    }

    /// <summary>
    /// Возвращает сводку по состояниям: сколько заданий в каком состоянии.
    /// </summary>
    private static string GetSummary(IReadOnlyCollection<OpeningTaskViewModel> tasks) {
        if(tasks.Count == 0) {
            return "Заданий на отверстия в файле нет.";
        }

        int attention = tasks.Count(task => task.NeedsAttention);
        return $"Заданий: {tasks.Count}. Требуют внимания: {attention}.";
    }

    private void Show(OpeningTaskViewModel? task) {
        if(task is null) {
            return;
        }

        _runner.Run(uiApplication => {
            var uiDocument = uiApplication.ActiveUIDocument;
            var ids = new List<ElementId> { task.Task.Id };
            uiDocument.Selection.SetElementIds(ids);
            uiDocument.ShowElements(ids);
        });
    }

    private void Refresh() {
        _runner.Run(Reload);
    }
}
