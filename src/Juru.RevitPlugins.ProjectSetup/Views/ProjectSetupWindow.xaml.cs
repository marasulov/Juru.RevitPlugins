using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

using Juru.RevitPlugins.ProjectSetup.Server;
using Juru.RevitPlugins.ProjectSetup.Services;

using Microsoft.Win32;

using Binding = System.Windows.Data.Binding;
using RevitApplication = Autodesk.Revit.ApplicationServices.Application;

namespace Juru.RevitPlugins.ProjectSetup.Views;

/// <summary>
/// Подготовка проекта целиком из Revit: проект и объекты заводятся здесь, папки и права
/// создаёт агент на сервере, а генплан, базовые файлы и файлы дисциплин — это же окно,
/// сразу после агента. Веб-страница остаётся для просмотра.
/// </summary>
/// <remarks>
/// Окно модальное и открывается из внешней команды, поэтому обработчики, в том числе
/// продолжения после await, выполняются в потоке Revit внутри команды — вызывать API Revit
/// из них можно.
/// </remarks>
public partial class ProjectSetupWindow : Window
{
    private static readonly TimeSpan AgentTimeout = TimeSpan.FromMinutes(3);

    private readonly RevitApplication _revit;
    private readonly string _serverUrl = SetupJobsClient.ReadDefaultUrl();
    private readonly PluginSettings _settings = PluginSettings.Load();
    private readonly ObservableCollection<ObjectRow> _objects = [];
    private readonly ObservableCollection<TemplateRow> _templates = [];

    private SetupApiClient? _api;
    private SetupConfig _config = new();
    private ProjectDto? _project;

    public ProjectSetupWindow(RevitApplication revit)
    {
        _revit = revit;
        InitializeComponent();

        ServerText.Text = _serverUrl;
        ObjectsGrid.ItemsSource = _objects;
        TemplatesList.ItemsSource = _templates;
        PasswordBox.Password = _settings.Password;

        Loaded += async (_, _) =>
        {
            if (PasswordBox.Password.Length > 0)
                await ConnectAsync();
            else
                Status("Введите пароль страницы настроек ClashTracker и нажмите «Подключиться».");
        };
    }

    // ---------- Подключение и список проектов ----------

    private async void OnConnect(object sender, RoutedEventArgs e) => await ConnectAsync();

    private async Task ConnectAsync()
    {
        await RunAsync("Подключение…", async () =>
        {
            _api?.Dispose();
            _api = new SetupApiClient(_serverUrl, PasswordBox.Password);
            _config = await _api.GetConfigAsync();

            _settings.Password = PasswordBox.Password;
            _settings.Save();

            BuildObjectColumns();
            await LoadProjectListAsync();
            NewProjectButton.IsEnabled = true;
            NewProject();
            Status("Подключено. Выберите проект слева или заведите новый.");
        });
    }

    private async Task LoadProjectListAsync(long? select = null)
    {
        var projects = await _api!.ListProjectsAsync();
        ProjectList.SelectionChanged -= OnProjectSelected;
        ProjectList.ItemsSource = projects;
        ProjectList.SelectedItem = select == null ? null : projects.FirstOrDefault(p => p.Id == select);
        ProjectList.SelectionChanged += OnProjectSelected;
    }

    private async void OnProjectSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ProjectList.SelectedItem is not ProjectListItem item) return;
        await RunAsync("Загрузка проекта…", async () =>
        {
            ShowProject(await _api!.GetProjectAsync(item.Id));
            Status($"Проект {item.FolderName}");
        });
    }

    private void OnNewProject(object sender, RoutedEventArgs e)
    {
        ProjectList.SelectedItem = null;
        NewProject();
    }

    private void NewProject()
    {
        _project = null;
        FolderBox.Text = "";
        CodeBox.Text = "";
        CentralBox.IsChecked = false;
        FolderBox.IsEnabled = CodeBox.IsEnabled = true;
        _objects.Clear();
        BuildTemplates(null);
        Editor.IsEnabled = true;
    }

    private void ShowProject(ProjectDto project)
    {
        _project = project;
        FolderBox.Text = project.FolderName;
        CodeBox.Text = project.Code;
        CentralBox.IsChecked = project.CreateAsCentral;

        // После первого этапа папки названы — имя и шифр менять нельзя, сервер это тоже не примет
        bool started = project.Jobs.Count > 0;
        FolderBox.IsEnabled = CodeBox.IsEnabled = !started;

        _objects.Clear();
        foreach (var o in project.Objects)
            _objects.Add(ObjectRow.Existing(o.Id, o.FolderName, o.Code, o.Stage, o.Disciplines, o.CreatedDisciplines));

        BuildTemplates(project);
        Editor.IsEnabled = true;
    }

    // ---------- Таблица объектов ----------

    private void BuildObjectColumns()
    {
        ObjectsGrid.Columns.Clear();
        ObjectsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Папка объекта", Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            Binding = new Binding(nameof(ObjectRow.FolderName)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }
        });
        ObjectsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Код / площадка", Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            Binding = new Binding(nameof(ObjectRow.Code)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }
        });
        foreach (string discipline in _config.Disciplines)
        {
            ObjectsGrid.Columns.Add(new DataGridCheckBoxColumn
            {
                Header = discipline, Width = DataGridLength.SizeToHeader,
                Binding = new Binding($"[{discipline}]") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }
            });
        }
        ObjectsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Этап", IsReadOnly = true, Binding = new Binding(nameof(ObjectRow.StageText))
        });
    }

    /// <summary>
    /// Запрет правок, которые сервер всё равно отклонит: имя и код объекта после этапа А,
    /// снятие дисциплины, файл которой уже создан.
    /// </summary>
    private void OnObjectBeginningEdit(object sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Row.Item is not ObjectRow row) return;
        string header = e.Column.Header?.ToString() ?? "";

        if (row.Locked && e.Column.DisplayIndex < 2)
            e.Cancel = true;
        else if (row.Created.Contains(header))
            e.Cancel = true;
    }

    private void OnAddObject(object sender, RoutedEventArgs e)
    {
        var row = new ObjectRow();
        // Новый объект — со всеми дисциплинами, кроме необязательной GFF, как и на веб-странице
        foreach (string d in _config.Disciplines.Where(d => !d.Equals("GFF", StringComparison.OrdinalIgnoreCase)))
            row[d] = true;
        _objects.Add(row);
        ObjectsGrid.SelectedItem = row;
    }

    private void OnRemoveObject(object sender, RoutedEventArgs e)
    {
        if (ObjectsGrid.SelectedItem is not ObjectRow row) return;
        if (row.Locked)
        {
            Status("Объект уже создан на диске — удалить его из проекта нельзя.", error: true);
            return;
        }
        _objects.Remove(row);
    }

    // ---------- Шаблоны ----------

    private void BuildTemplates(ProjectDto? project)
    {
        _templates.Clear();
        string version = _revit.VersionNumber;

        void Add(string key, string label, string? stored)
        {
            _config.TemplateFolders.TryGetValue(key, out string? folder);
            var row = new TemplateRow { Key = key, Label = label, Folder = folder };

            if (!string.IsNullOrWhiteSpace(stored) && File.Exists(stored))
            {
                row.Path = stored!;
                row.SetNote("", false);
            }
            else
            {
                // Пути нет или он устарел (папку шаблонов переименовали) — подбираем заново
                var choice = TemplateResolver.Resolve(folder, version);
                row.Path = choice.Path ?? stored ?? "";
                string prefix = string.IsNullOrWhiteSpace(stored) ? "" : "прежний путь не найден; ";
                row.SetNote(prefix + choice.Note, choice.Path == null);
            }

            _templates.Add(row);
        }

        Add("GEN", "Генплан", project?.GenplanTemplate);
        Add("CRD", "Базовый файл", project?.BaseTemplate);
        foreach (string d in _config.Disciplines)
            Add(d, d, project?.DisciplineTemplates.TryGetValue(d, out string? t) == true ? t : null);
    }

    private void OnBrowseTemplate(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TemplateRow row) return;

        string? initial = File.Exists(row.Path) ? Path.GetDirectoryName(row.Path) : row.Folder;
        var dialog = new OpenFileDialog
        {
            Title = $"Шаблон: {row.Label}",
            Filter = "Шаблоны Revit (*.rte)|*.rte",
            InitialDirectory = initial != null && Directory.Exists(initial) ? initial : ""
        };

        if (dialog.ShowDialog(this) == true)
        {
            row.Path = dialog.FileName;
            row.SetNote("выбран вручную", false);
        }
    }

    // ---------- Сохранение и этапы ----------

    private async void OnSave(object sender, RoutedEventArgs e) =>
        await RunAsync("Сохранение…", async () =>
        {
            await SaveAsync();
            Status("Сохранено.");
        });

    private async Task SaveAsync()
    {
        var request = new SaveProjectRequest
        {
            FolderName = FolderBox.Text.Trim(),
            Code = CodeBox.Text.Trim(),
            GenplanTemplate = TemplatePath("GEN"),
            BaseTemplate = TemplatePath("CRD"),
            CreateAsCentral = CentralBox.IsChecked == true,
            DisciplineTemplates = _templates.Where(t => t.Key is not ("GEN" or "CRD") && t.Path.Length > 0)
                .ToDictionary(t => t.Key, t => t.Path),
            Objects = _objects.Select(o => new SaveObjectRequest
            {
                Id = o.Id, FolderName = o.FolderName.Trim(), Code = o.Code.Trim(), Disciplines = o.Disciplines
            }).ToList()
        };

        var saved = await _api!.SaveProjectAsync(_project?.Id, request);
        ShowProject(saved);
        await LoadProjectListAsync(saved.Id);
    }

    private string? TemplatePath(string key) =>
        _templates.FirstOrDefault(t => t.Key == key) is { Path.Length: > 0 } row ? row.Path : null;

    private async void OnStageA(object sender, RoutedEventArgs e) => await RunStageAsync("stage-a", "Этап А");

    private async void OnStageB(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this,
            "Этап Б берёт координаты с площадок базовых файлов.\n\n" +
            "Базовые файлы расставлены в генплане, и координаты опубликованы (Publish) с площадкой, названной кодом объекта?",
            "Этап Б", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes)
            await RunStageAsync("stage-b", "Этап Б");
    }

    private async void OnRunQueue(object sender, RoutedEventArgs e) =>
        await RunAsync("Выполнение заданий…", RunRevitJobsAsync);

    /// <summary>
    /// Этап целиком: сохранить, поставить в очередь, дождаться агента (папки и права на сервере)
    /// и сразу выполнить Revit-часть здесь же.
    /// </summary>
    private async Task RunStageAsync(string stage, string title)
    {
        await RunAsync($"{title}: сохранение…", async () =>
        {
            await SaveAsync();
            var response = await _api!.RunStageAsync(_project!.Id, stage);
            Log($"{title}: поставлены задания {string.Join(", ", response.Jobs.Select(j => "#" + j))}");

            if (!await WaitForAgentAsync(response.Jobs, title))
                return;

            await RunRevitJobsAsync();
        });
    }

    /// <summary>Ждёт задание папок; false — агент не справился или не ответил, Revit-часть не запускаем.</summary>
    private async Task<bool> WaitForAgentAsync(System.Collections.Generic.List<long> jobIds, string title)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            var project = await _api!.GetProjectAsync(_project!.Id);
            var fsJob = project.Jobs.FirstOrDefault(j => jobIds.Contains(j.Id) && j.Executor == "fs");
            if (fsJob == null || fsJob.Status == "done")
                return true;

            if (fsJob.Status == "failed")
            {
                Log(fsJob.Log ?? "");
                Status($"{title}: агент на сервере не смог создать папки — подробности в журнале.", error: true);
                ShowProject(project);
                return false;
            }

            if (watch.Elapsed > AgentTimeout)
            {
                Status($"{title}: агент на сервере не ответил за {AgentTimeout.TotalMinutes:0} мин. Задания остались в очереди; " +
                       "проверьте задачу ClashTrackerSetupAgent на сервере и нажмите «Выполнить задания из очереди».", error: true);
                return false;
            }

            Status($"{title}: агент на сервере создаёт папки и права… {watch.Elapsed.TotalSeconds:0} с");
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }

    /// <summary>
    /// Выполняет Revit-задания из очереди. Если нашлись уже существующие файлы, спрашивает,
    /// пропустить ли их, и выполняет снова: перезаписи нет.
    /// </summary>
    private async Task RunRevitJobsAsync()
    {
        for (int round = 0; round < 3; round++)
        {
            Status("Создание файлов в Revit… (окно прогресса поверх Revit)");
            using (var client = new SetupJobsClient(_serverUrl, $"{Environment.MachineName}\\{Environment.UserName} (Revit {_revit.VersionNumber})"))
            {
                foreach (string line in new SetupJobRunner(client, new RevitFiles(_revit)).RunAll())
                    Log(line);
            }

            var project = await _api!.GetProjectAsync(_project!.Id);
            ShowProject(project);

            var waiting = project.Jobs.Where(j => j.Executor == "revit" && j.Status == "needs_confirmation").ToList();
            if (waiting.Count == 0)
            {
                // Сервер отдаёт задания от новых к старым: первое Revit-задание — только что выполненное
                var latest = project.Jobs.FirstOrDefault(j => j.Executor == "revit");
                bool withErrors = latest?.Status == "failed" || project.Objects.Any(o => o.Stage != "ready" && latest?.Kind == JobKinds.StageB);
                Status(withErrors ? "Готово с ошибками — подробности в журнале." : "Готово.", error: withErrors);
                return;
            }

            var conflicts = waiting.SelectMany(Conflicts).ToList();
            var answer = MessageBox.Show(this,
                "Эти файлы уже существуют:\n\n" + string.Join("\n", conflicts.Take(20)) + (conflicts.Count > 20 ? $"\n… и ещё {conflicts.Count - 20}" : "") +
                "\n\nПропустить их и создать остальные? Существующие файлы не перезаписываются никогда.",
                "Файлы уже есть", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
            {
                Status("Остановлено: решение по существующим файлам можно принять позже — здесь или на странице проекта.");
                return;
            }

            foreach (var job in waiting)
                await _api.SkipExistingAsync(job.Id);
        }
    }

    private static System.Collections.Generic.IEnumerable<string> Conflicts(ProjectJobDto job) =>
        job.Result is { ValueKind: JsonValueKind.Object } r && r.TryGetProperty("conflicts", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(x => x.GetString() ?? "")
            : [];

    // ---------- Общее ----------

    /// <summary>Действие с блокировкой кнопок и понятной ошибкой вместо падения окна.</summary>
    private async Task RunAsync(string busyText, Func<Task> action)
    {
        SetBusy(true);
        Status(busyText);
        try
        {
            await action();
        }
        catch (SetupApiException ex) when (ex.IsUnauthorized)
        {
            Status("Неверный пароль или страница подготовки на сервере выключена.", error: true);
        }
        catch (SetupApiException ex)
        {
            string details = ex.Errors.Count > 0 ? string.Join("\n", ex.Errors.Values.Distinct()) : ex.Message;
            Status(details, error: true);
        }
        catch (Exception ex)
        {
            Status("Ошибка: " + ex.Message, error: true);
            Log(ex.ToString());
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        ConnectButton.IsEnabled = !busy;
        SaveButton.IsEnabled = StageAButton.IsEnabled = StageBButton.IsEnabled = QueueButton.IsEnabled = !busy;
        ProjectList.IsEnabled = !busy;
        Cursor = busy ? System.Windows.Input.Cursors.Wait : null;
    }

    private void Status(string text, bool error = false)
    {
        StatusText.Text = text;
        StatusText.Foreground = error ? Brushes.Firebrick : Brushes.Black;
        if (error) Log(text);
    }

    private void Log(string line)
    {
        if (string.IsNullOrEmpty(line)) return;
        LogBox.AppendText($"{DateTime.Now:HH:mm:ss} {line}{Environment.NewLine}");
        LogBox.ScrollToEnd();
    }
}
