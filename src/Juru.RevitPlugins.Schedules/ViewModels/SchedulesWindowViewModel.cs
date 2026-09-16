using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Schedules.Localization;
using Juru.RevitPlugins.Schedules.Models;
using Juru.RevitPlugins.Schedules.Services;
using Juru.RevitPlugins.Schedules.Services.Categories;
using Juru.RevitPlugins.Schedules.Services.Translation;

namespace Juru.RevitPlugins.Schedules.ViewModels;

/// <summary>
///     Окно выбора спецификаций для английского комплекта.
/// </summary>
public sealed partial class SchedulesWindowViewModel : ObservableObject
{
    private readonly ICollectionView scheduleView;
    private readonly ISchedulesHost? host;
    private readonly Dictionary<string, CategoryViewModel> categoriesByKey;

    private SchedulesConfig config;

    /// <summary>Сколько прошло с прошлой прокрутки очереди: чаще перерисовывать окно незачем.</summary>
    private readonly System.Diagnostics.Stopwatch pumped = new();

    private const int PumpIntervalMilliseconds = 100;

    /// <summary>
    ///     Ключ активной вкладки. Кнопки выбора, перевода и показа одни на окно и работают по ней -
    ///     иначе те же кнопки дублировались бы на каждой из четырнадцати вкладок.
    /// </summary>
    private string surface = SchedulesSurface;

    [ObservableProperty]
    private string searchText = string.Empty;

    /// <summary>
    ///     Читать только активный вид, а не всю модель. На большой модели разница заметная, а для
    ///     листа обычно и нужен один вид.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScanWholeModel))]
    private bool scanActiveViewOnly;

    /// <summary>
    ///     Обратное к <see cref="ScanActiveViewOnly" />, своё у второго переключателя области.
    /// </summary>
    /// <remarks>
    ///     Два переключателя на одном свойстве не работают: группа снимает отметку со второго сама,
    ///     и это <c>false</c> уходит обратно по привязке. Через обратный преобразователь он
    ///     превращался во «включить только активный вид» - область перескакивала при открытии окна,
    ///     и заодно дёргалось лишнее чтение модели. Поэтому у каждого переключателя своё свойство, а
    ///     снятие отметки (<c>false</c>) здесь просто игнорируется: выбор делает тот, кого включили.
    /// </remarks>
    public bool ScanWholeModel
    {
        get { return !ScanActiveViewOnly; }
        set
        {
            if (value)
            {
                ScanActiveViewOnly = false;
            }
        }
    }

    /// <summary>
    ///     Показывать все спецификации модели, а не только размещённые на листах.
    /// </summary>
    /// <remarks>
    ///     По умолчанию окно показывает выпускаемый комплект - то, что стоит на листах. Переводят,
    ///     однако, и неразмещённые: заготовку под будущий лист или спецификацию, которую печатают из
    ///     вида. Отбор до листа сильнее: с областью «активный вид» на листе речь идёт именно про этот
    ///     лист, и неразмещённые в него не попадают.
    /// </remarks>
    [ObservableProperty]
    private bool allSchedules;

    /// <summary>Сколько прочитано и сколько всего: полоса показывает это, а не бежит вслепую.</summary>
    [ObservableProperty]
    private int readTotal;

    [ObservableProperty]
    private int readDone;

    /// <summary>Модель ещё читается: окно уже открыто, а таблицы пусты.</summary>
    /// <remarks>
    ///     Доступность кнопки сборки уведомляется атрибутом, а не вызовом руками: WPF кеширует
    ///     <c>CanExecute</c> до события <c>CanExecuteChanged</c>, и путь, где его забыли поднять,
    ///     оставляет кнопку серой навсегда.
    /// </remarks>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
    private bool isLoading = true;

    /// <summary>Строка состояния под заголовком: что читается сейчас или почему таблицы пусты.</summary>
    [ObservableProperty]
    private string statusNote = Text.StatusReadingSchedules;

    /// <param name="host">
    ///     Всё, что нужно от модели. Задаёт команда: окно не знает ни документа, ни вида.
    /// </param>
    /// <remarks>
    ///     Окно создаётся пустым и открывается сразу, а строки приходят в <see cref="Fill" /> уже
    ///     после этого: чтение модели идёт секунды, и всё это время Ревит выглядел бы зависшим.
    /// </remarks>
    public SchedulesWindowViewModel(SchedulesConfig config, ISchedulesHost? host = null)
    {
        this.host = host;
        this.config = config ?? throw new ArgumentNullException(nameof(config));

        Rows = [];

        scheduleView = new CollectionViewSource { Source = Rows }.View;
        scheduleView.Filter = MatchesSchedule;

        // Служба перевода берётся у окна при каждом запросе: пользователь меняет её в выпадающем списке
        // уже после того, как вкладка построена.
        Categories = CategoryRegistry.All
            .Select(category => Watch(new CategoryViewModel(
                category.Key,
                category.Name,
                [],
                category.EmptyNote,
                () => this.config.Translation,
                host is null ? null : host.ShowElement)))
            .ToList();

        categoriesByKey = Categories.ToDictionary(category => category.Key, StringComparer.Ordinal);
    }

    /// <summary>
    ///     Подписывает окно на сводку вкладки: нижняя строка одна, и она обязана обновляться, когда
    ///     на вкладке ставят галочки или переводят строки.
    /// </summary>
    private CategoryViewModel Watch(CategoryViewModel category)
    {
        category.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName is nameof(CategoryViewModel.Summary))
            {
                OnPropertyChanged(nameof(Summary));
            }
        };

        return category;
    }

    /// <summary>
    ///     Вкладка категории по устойчивому ключу. Пусто, если такой категории в реестре нет.
    /// </summary>
    /// <remarks>
    ///     По ключу, а не по имени: имя переводится, и поиск по нему сломался бы при английском
    ///     интерфейсе — вкладки молча остались бы пустыми.
    /// </remarks>
    public CategoryViewModel? Category(string key)
    {
        return categoriesByKey.TryGetValue(key, out var found) ? found : null;
    }

    /// <summary>
    ///     Заполняет окно прочитанным и снимает состояние загрузки.
    /// </summary>
    public void Fill(
        IReadOnlyList<ScheduleRowViewModel> rows,
        IReadOnlyDictionary<string, IReadOnlyList<ElementTextRowViewModel>> categories)
    {
        if (rows is null)
        {
            throw new ArgumentNullException(nameof(rows));
        }

        foreach (var row in Rows)
        {
            row.PropertyChanged -= OnRowChanged;
        }

        foreach (var row in rows)
        {
            // Счётчик внизу считается по галочкам, а галочку переключают и мимо команд окна - прямо
            // в таблице. Подписка на строки - единственное место, где видно любое такое переключение.
            row.PropertyChanged += OnRowChanged;
        }

        // Целиком, а не построчно: иначе таблица заводит контейнер под каждую строку и применяет
        // к нему шаблоны, тогда как при одном Reset контейнеры заводятся только под видимые.
        Rows.Reset(rows);

        foreach (var category in Categories)
        {
            category.Fill(categories.TryGetValue(category.Key, out var found) ? found : []);
        }

        scheduleView.Refresh();

        IsLoading = false;
        StatusNote = string.Empty;

        OnPropertyChanged(nameof(Summary));
    }

    public RowCollection<ScheduleRowViewModel> Rows { get; }

    /// <summary>Вкладки категорий модели в порядке реестра.</summary>
    public IReadOnlyList<CategoryViewModel> Categories { get; }

    /// <summary>Вкладки категорий поимённо: разметка привязывается к ним, а не к номеру в списке.</summary>
    public CategoryViewModel? Rooms
    {
        get { return Category("Rooms"); }
    }

    public CategoryViewModel? Areas
    {
        get { return Category("Areas"); }
    }

    public CategoryViewModel? Sheets
    {
        get { return Category("Sheets"); }
    }

    public CategoryViewModel? Views
    {
        get { return Category("Views"); }
    }

    public CategoryViewModel? Notes
    {
        get { return Category("TextNotes"); }
    }

    public CategoryViewModel? Levels
    {
        get { return Category("Levels"); }
    }

    public CategoryViewModel? Materials
    {
        get { return Category("Materials"); }
    }

    public CategoryViewModel? Revisions
    {
        get { return Category("Revisions"); }
    }

    public CategoryViewModel? Dimensions
    {
        get { return Category("Dimensions"); }
    }

    public CategoryViewModel? FamilyTypes
    {
        get { return Category("FamilyTypes"); }
    }

    public CategoryViewModel? Keynotes
    {
        get { return Category("Keynotes"); }
    }

    public CategoryViewModel? GlobalParameters
    {
        get { return Category("GlobalParameters"); }
    }

    /// <summary>
    ///     Что сделать с отмеченными спецификациями: копия линиями, копия спецификацией или перевод в
    ///     самих спецификациях.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RedrawAsLines))]
    [NotifyPropertyChangedFor(nameof(CopyAsSchedule))]
    private ScheduleCopyMode copyMode = ScheduleCopyMode.RedrawAsLines;

    /// <summary>
    ///     Переключатели способа: у каждого своё свойство, а снятие отметки игнорируется - выбор делает
    ///     тот, кого включили. То же правило, что у области сканирования, и по той же причине: общее
    ///     свойство на группу переворачивалось бы её собственным <c>false</c>.
    /// </summary>
    public bool RedrawAsLines
    {
        get { return CopyMode is ScheduleCopyMode.RedrawAsLines; }
        set { Select(value, ScheduleCopyMode.RedrawAsLines); }
    }

    public bool CopyAsSchedule
    {
        get { return CopyMode is ScheduleCopyMode.CopyAsSchedule; }
        set { Select(value, ScheduleCopyMode.CopyAsSchedule); }
    }

    private void Select(bool isChecked, ScheduleCopyMode mode)
    {
        if (isChecked)
        {
            CopyMode = mode;
        }
    }

    public ICollectionView View
    {
        get { return scheduleView; }
    }

    /// <summary>Вкладка выбора спецификаций.</summary>
    public const string SchedulesSurface = "Schedules";

    /// <summary>
    ///     Запоминает открытую вкладку. Зовётся из окна: какая вкладка открыта, знает разметка.
    /// </summary>
    public void UseSurface(string key)
    {
        surface = key;

        OnPropertyChanged(nameof(Summary));
    }

    /// <summary>Службы перевода: выбор оставлен в окне, чтобы не заходить за ним в настройки.</summary>
    public IReadOnlyList<string> Providers { get; } =
        [TranslationSettings.DeepL, TranslationSettings.Google];

    /// <summary>
    ///     Выбранная служба. Пишется прямо в настройки: это их часть, а не состояние окна, и выбор
    ///     должен сохраниться до следующего запуска.
    /// </summary>
    public string Provider
    {
        get { return config.Translation.Provider; }
        set
        {
            if (string.Equals(config.Translation.Provider, value, StringComparison.Ordinal))
            {
                return;
            }

            config = config with { Translation = config.Translation with { Provider = value } };

            config.TrySave();

            OnPropertyChanged();
        }
    }

    /// <summary>Языки для полей «От» и «До»: тот же список, что раньше показывался в настройках.</summary>
    public IReadOnlyList<LanguageOption> Languages { get; } = LanguageOptions.All;

    /// <summary>
    ///     Исходный язык. Пишется прямо в настройки, как <see cref="Provider" />: это их часть, а не
    ///     состояние окна.
    /// </summary>
    public string SourceLanguage
    {
        get { return config.Translation.SourceLanguage; }
        set
        {
            if (string.Equals(config.Translation.SourceLanguage, value, StringComparison.Ordinal))
            {
                return;
            }

            config = config with { Translation = config.Translation with { SourceLanguage = value } };

            config.TrySave();

            OnPropertyChanged();
        }
    }

    /// <summary>
    ///     Целевой язык. Его код входит и в запрос к службе перевода, и в имя файла английской
    ///     таблицы ключевых примечаний.
    /// </summary>
    public string TargetLanguage
    {
        get { return config.Translation.TargetLanguage; }
        set
        {
            if (string.Equals(config.Translation.TargetLanguage, value, StringComparison.Ordinal))
            {
                return;
            }

            config = config with { Translation = config.Translation with { TargetLanguage = value } };

            config.TrySave();

            OnPropertyChanged();
        }
    }

    /// <summary>Настройки, по которым идёт перевод. Меняются в окне настроек.</summary>
    public SchedulesConfig Config
    {
        get { return config; }
    }

    /// <summary>
    ///     Принимает изменённые настройки: колонки «Что запишется» пересчитываются сразу, чтобы
    ///     виден был новый вид значения, а не тот, что был до правки.
    /// </summary>
    public void ApplySettings(SchedulesConfig updated)
    {
        config = updated ?? throw new ArgumentNullException(nameof(updated));

        OnPropertyChanged(nameof(Provider));
        OnPropertyChanged(nameof(SourceLanguage));
        OnPropertyChanged(nameof(TargetLanguage));

        // Режим записи и разделитель теперь тоже часть настроек - колонки «Что запишется»
        // пересчитываются сразу, чтобы виден был новый вид значения, а не тот, что был до правки.
        RefreshResults();
    }

    /// <summary>
    ///     Писать в параметр исходник и перевод рядом, а не только перевод: тогда один и тот же
    ///     параметр читается и в русском, и в английском комплекте. Меняется в настройках.
    /// </summary>
    private bool KeepSourceText
    {
        get { return config.Translation.KeepSourceText; }
    }

    private string Separator
    {
        get { return config.Translation.Separator; }
    }

    /// <summary>Как собирать записываемое значение. Читается строками таблиц при каждой отрисовке.</summary>
    public TranslationLayout Layout
    {
        get { return new TranslationLayout { KeepSource = KeepSourceText, Separator = Separator }; }
    }

    public IReadOnlyList<ScheduleRowViewModel> SelectedRows
    {
        get { return Rows.Where(row => row.IsSelected).ToList(); }
    }

    /// <summary>
    ///     Вычитанные в окне переводы в виде строк глоссария. Пустые тоже попадают: их добирает
    ///     машинный перевод при сборке.
    /// </summary>
    /// <remarks>
    ///     Непустые помечаются как правленные человеком: пользователь видел их в окне и оставил, то
    ///     есть согласовал, и машинный перевод не должен их перетирать.
    /// </remarks>
    public IReadOnlyList<GlossaryEntry> GlossaryEntries
    {
        get
        {
            // Строки спецификаций сюда не входят: их собирает сама сборка из перечитанных
            // спецификаций, а правят их в окне словаря.
            return Categories
                .SelectMany(category => category.SelectedRows)
                .Select(row => Entry(row.SourceText, row.SourceText, row.Translation))
                .GroupBy(entry => entry.Key, StringComparer.CurrentCulture)
                .Select(group => group.FirstOrDefault(entry => entry.Suggestion.Length > 0) ?? group.First())
                .ToList();
        }
    }

    /// <remarks>
    ///     Непустой перевод помечается правленным человеком: пользователь видел его в окне и оставил,
    ///     то есть согласовал, и машинный перевод не должен его перетирать.
    /// </remarks>
    private static GlossaryEntry Entry(string key, string readable, string translation)
    {
        var trimmed = translation.Trim();

        return new GlossaryEntry
        {
            Key = key,
            Readable = readable,
            Suggestion = trimmed,
            Source = trimmed.Length > 0 ? TranslationSource.Human : TranslationSource.Machine,
            Occurrences = 0,
            Schedules = []
        };
    }

    /// <summary>Отмеченное по всем категориям и собранные значения. Пустые категории отброшены.</summary>
    public IReadOnlyList<CategoryWrite> SelectedCategories
    {
        get
        {
            return Categories
                .Select(category => new CategoryWrite(
                    category.Key,
                    category.Name,
                    category.SelectedRows
                        .Select(row => new CategoryValue(row.Text, row.Translation))
                        .ToList()))
                .Where(write => write.Values.Count > 0)
                .ToList();
        }
    }

    /// <summary>Сколько строк отмечено во всех категориях модели.</summary>
    public int SelectedCategoryCount
    {
        get { return Categories.Sum(category => category.SelectedRows.Count); }
    }

    /// <summary>Идёт сборка: кнопки на это время гаснут, чтобы её не запустили второй раз.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
    private bool isBusy;

    /// <summary>
    ///     Открывает спецификацию строки под курсором, чтобы её было видно в Ревите.
    /// </summary>
    [RelayCommand]
    private void ShowSchedule(ScheduleRowViewModel? row)
    {
        // Кнопка стоит в строке и передаёт её параметром; из панели окна параметра нет, и тогда
        // берётся строка под курсором таблицы.
        var target = row ?? scheduleView.CurrentItem as ScheduleRowViewModel;

        if (target is null)
        {
            StatusNote = Text.StatusPickScheduleRow;
            return;
        }

        StatusNote = string.Empty;
        host?.ShowView(target.Read.Source.Schedule.Id);
    }

    /// <summary>Собирает английский комплект по отмеченному.</summary>
    [RelayCommand(CanExecute = nameof(CanBuild))]
    private void Build()
    {
        if (SelectedRows.Count is 0 && SelectedCategoryCount is 0)
        {
            StatusNote = Text.StatusNothingChecked;
            return;
        }

        host?.Build();
    }

    private bool CanBuild()
    {
        return !IsBusy && !IsLoading;
    }

    /// <summary>
    ///     Показывает, что читается сейчас, и отдаёт управление очереди окна.
    /// </summary>
    /// <remarks>
    ///     Чтение идёт в потоке Ревита, а его же обслуживает окно: без прокрутки очереди ни строка
    ///     состояния не обновится, ни бегущая полоса не сдвинется — окно просто стоит, пока читается
    ///     модель. Прокрутка на приоритете <c>Render</c>: анимации нужен именно проход отрисовки.
    /// </remarks>
    public void Reading(string name)
    {
        ReadDone++;
        StatusNote = $"{Text.StatusReadingSchedules} {name}";

        Pump();
    }

    /// <summary>
    ///     Прокручивает очередь окна, но не чаще, чем нужно глазу.
    /// </summary>
    /// <remarks>
    ///     Прокрутка на приоритете <c>Render</c> — это полный проход разметки и отрисовки окна с
    ///     тринадцатью таблицами. На спецификацию такой проход стоит больше, чем само чтение
    ///     спецификации: на модели с семью десятками спецификаций чтение занимало миллисекунды, а
    ///     окно перерисовывалось семьдесят раз. Глазу этого не нужно — полоса, которая двигается
    ///     десять раз в секунду, выглядит так же плавно, как двигающаяся семьдесят.
    /// </remarks>
    private void Pump()
    {
        if (pumped.IsRunning && pumped.ElapsedMilliseconds < PumpIntervalMilliseconds)
        {
            return;
        }

        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);

        pumped.Restart();
    }

    /// <summary>
    ///     Начинает чтение: полоса становится определённой.
    /// </summary>
    /// <remarks>
    ///     Именно определённой, а не бегущей: чтение занимает поток Ревита, а его же обслуживает
    ///     окно, поэтому анимации не достаётся кадров и «бегущая» полоса просто стоит. Полоса,
    ///     которую двигаем мы сами на каждой прочитанной спецификации, двигается по-настоящему.
    /// </remarks>
    public void StartReading(int total)
    {
        ReadTotal = Math.Max(total, 1);
        ReadDone = 0;
        IsLoading = true;
        StatusNote = ScanActiveViewOnly ? Text.StatusReadingScope : Text.StatusReadingSchedules;

        pumped.Reset();
        Pump();
    }

    /// <summary>
    ///     Снимает состояние загрузки, когда прочитать не удалось: окно остаётся пустым, но
    ///     работоспособным, а не висит с серой кнопкой без объяснения.
    /// </summary>
    public void FinishLoading(string note)
    {
        IsLoading = false;
        StatusNote = note;
    }

    /// <summary>Начало и конец длинной работы: кнопка сборки гаснет и снова включается.</summary>
    public void SetBusy(bool busy, string note)
    {
        IsBusy = busy;
        StatusNote = note;
    }

    /// <summary>
    ///     Сводка по открытой вкладке. Одна на окно: своя сводка под каждой из четырнадцати таблиц
    ///     занимала бы строку и говорила бы то же самое.
    /// </summary>
    public string Summary
    {
        get
        {
            if (Category(surface) is { } category)
            {
                return category.Summary;
            }

            var selected = Rows.Count(row => row.IsSelected);
            var untranslated = Rows.Where(row => row.IsSelected).Sum(row => row.UntranslatedCount);

            return string.Format(Text.SummarySchedules, selected, Rows.Count, untranslated);
        }
    }

    /// <summary>
    ///     Отмечает то, что видно после поиска: иначе галочки встают и на скрытых строках, а пользователь
    ///     этого не видит.
    /// </summary>
    [RelayCommand]
    private void SelectAll()
    {
        SetSelection(true);
    }

    [RelayCommand]
    private void SelectNone()
    {
        SetSelection(false);
    }

    /// <summary>
    ///     Отмечает видимое на активной вкладке. Скрытые поиском и фильтром строки не трогаются:
    ///     галочки встали бы там, где пользователь их не видит.
    /// </summary>
    private void SetSelection(bool isSelected)
    {
        if (Category(surface) is { } category)
        {
            if (isSelected)
            {
                category.SelectAllCommand.Execute(null);
            }
            else
            {
                category.SelectNoneCommand.Execute(null);
            }

            return;
        }

        foreach (var row in scheduleView.Cast<ISelectableRow>().ToList())
        {
            row.IsSelected = isSelected;
        }
    }

    /// <summary>
    ///     Показывает в Ревите то, что выбрано на активной вкладке: спецификацию или элемент модели.
    /// </summary>
    [RelayCommand]
    private void Show()
    {
        if (Category(surface) is { } category)
        {
            category.ShowSelectedCommand.Execute(null);
            StatusNote = category.Note;

            return;
        }

        ShowScheduleCommand.Execute(null);
    }

    /// <summary>
    ///     Переводит машиной пустые переводы на активной вкладке.
    /// </summary>
    [RelayCommand]
    private async Task TranslateMissingAsync()
    {
        if (Category(surface) is { } category)
        {
            await category.TranslateMissingCommand.ExecuteAsync(null).ConfigureAwait(true);

            StatusNote = category.Note;
            return;
        }

        // Спецификации переводятся при сборке: их строки идут через словарь целиком, а правят
        // словарь в своём окне.
        StatusNote = Text.NoteNotOnThisTab;

        await Task.CompletedTask.ConfigureAwait(true);
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(ScheduleRowViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(Summary));
        }
    }

    private bool MatchesSchedule(object item)
    {
        if (SearchText.Length is 0)
        {
            return true;
        }

        if (item is not ScheduleRowViewModel row)
        {
            return false;
        }

        return Contains(row.ScheduleName) || Contains(row.Sheets);
    }

    private bool Contains(string text)
    {
        return text.IndexOf(SearchText, StringComparison.CurrentCultureIgnoreCase) >= 0;
    }

    /// <summary>Поиск один на окно и фильтрует обе таблицы, чтобы не искать заново при смене вкладки.</summary>
    /// <summary>Пересчитывает колонки «Что запишется» на всех вкладках.</summary>
    private void RefreshResults()
    {
        foreach (var category in Categories)
        {
            category.RefreshResults();
        }
    }

    /// <summary>
    ///     Пересчитывает «Без перевода» по словарю: его правят в отдельном окне, и после правки
    ///     колонка иначе показывала бы то, что было до неё.
    /// </summary>
    public void RefreshAgainstGlossary()
    {
        var glossary = GlossaryFile.Read(config.ResolveGlossaryPath());

        foreach (var row in Rows)
        {
            row.RefreshAgainst(glossary);
        }

        OnPropertyChanged(nameof(Summary));
    }

    /// <summary>Смена области сканирования - это новое чтение: список текста будет другой.</summary>
    partial void OnScanActiveViewOnlyChanged(bool value)
    {
        host?.Load();
    }

    /// <summary>Смена набора спецификаций - тоже новое чтение: список строк будет другой.</summary>
    partial void OnAllSchedulesChanged(bool value)
    {
        host?.Load();
    }

    partial void OnSearchTextChanged(string value)
    {
        scheduleView.Refresh();

        foreach (var category in Categories)
        {
            category.ApplyFilter(text => SearchText.Length is 0 || Contains(text));
        }
    }
}
