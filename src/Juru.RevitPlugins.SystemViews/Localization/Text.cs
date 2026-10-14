using Autodesk.Revit.ApplicationServices;

namespace Juru.RevitPlugins.SystemViews.Localization;

/// <summary>
///     Строки интерфейса на двух языках.
/// </summary>
/// <remarks>
///     Тот же подход, что в <c>Schedules.Localization.Text</c>: оба языка рядом, без ключей. Язык
///     берётся у Ревита до создания окна, разметка читает строки через <c>x:Static</c>.
/// </remarks>
public static class Text
{
    private static bool russian = true;

    /// <summary>Русский интерфейс Ревита - русский плагин, любой другой - английский.</summary>
    public static void Use(LanguageType language)
    {
        russian = language is LanguageType.Russian;
    }

    private static string Pick(string ru, string en)
    {
        return russian ? ru : en;
    }

    // --- Окно

    public static string WindowTitle => Pick("Виды по системам", "System views");

    public static string SourceView => Pick("Исходный вид:", "Source view:");

    public static string CategoriesHeader => Pick("Категории", "Categories");

    public static string CategoriesSummary => Pick("Категории: выбрано {0} из {1}", "Categories: {0} of {1} selected");

    public static string Hvac => Pick("ОВ", "HVAC");

    public static string Plumbing => Pick("ВК", "Plumbing");

    public static string Electrical => Pick("ЭОМ", "Electrical");

    public static string Parameter => Pick("Параметр системы:", "System parameter:");

    public static string ParameterCategories => Pick("{0}  ·  {1} из {2} категорий", "{0}  ·  {1} of {2} categories");

    public static string SystemsHeader => Pick("Системы", "Systems");

    public static string Search => Pick("Поиск системы", "Find a system");

    public static string SelectAll => Pick("Выбрать все", "Select all");

    public static string SelectNone => Pick("Снять все", "Clear all");

    public static string ElementCount => Pick("{0} эл.", "{0} el.");

    public static string WillUpdate => Pick("обновится", "will update");

    public static string WarningFew => Pick("⚠ почти пустая", "⚠ nearly empty");

    public static string WarningTip => Pick(
        "В системе всего элементов: {0}. Похоже на обрывок: например, неподключённая муфта, которую Ревит вынес в отдельную систему. «Выбрать все» такие системы не отмечает.",
        "The system has only {0} elements. Looks like a fragment: e.g. an unconnected fitting Revit split into its own system. «Select all» skips such systems.");

    public static string SummarySuspicious => Pick("  ·  почти пустых: {0}", "  ·  nearly empty: {0}");

    public static string NoSystems => Pick(
        "У элементов вида параметр не заполнен. Выберите другой параметр или категории.",
        "The parameter is empty on the view's elements. Pick another parameter or categories.");

    public static string ViewNamePrefix => Pick("Префикс имени вида:", "View name prefix:");

    public static string Template => Pick("Базовый шаблон:", "Base template:");

    public static string TemplateAsSource => Pick("Как у исходного вида", "Same as the source view");

    public static string TemplateNone => Pick("Без шаблона", "No template");

    public static string FitSectionBox => Pick("Подрезать 3D-рамку по системе", "Fit the section box to the system");

    public static string FitSectionBoxHint => Pick(
        "Рамка охватывает элементы системы с запасом 500 мм",
        "The box covers the system's elements with a 500 mm margin");

    public static string Summary => Pick("Выбрано систем: {0}  ·  новых видов: {1}  ·  обновится: {2}", "Systems selected: {0}  ·  new views: {1}  ·  to update: {2}");

    public static string Hint => Pick(
        "На схеме видна только своя система: чужие системы скрыты фильтром, остальные категории, связи, уровни и оси - скрыты. С базовым шаблоном каждая схема получает свой шаблон «<базовый> - <система>». Значение «П1, В1» относится к обеим системам; «В1» не совпадает с «В10».",
        "A view shows only its own system: other systems are hidden by a filter; other categories, links, levels and grids are hidden. With a base template each view gets its own template «<base> - <system>». A value «S1, E1» belongs to both systems; «E1» does not match «E10».");

    public static string Create => Pick("Создать", "Create");

    public static string Cancel => Pick("Отмена", "Cancel");

    // --- Команда

    public static string NeedView3D => Pick(
        "Откройте 3D-вид с инженерными системами: схемы делаются его копиями.",
        "Open a 3D view with the MEP systems: the system views are copies of it.");

    public static string NoMepElements => Pick(
        "На открытом виде нет инженерных элементов.",
        "The open view has no MEP elements.");

    public static string TransactionName => Pick("Виды по системам", "System views");

    public static string CannotDuplicate => Pick("Исходный вид нельзя скопировать.", "The source view cannot be duplicated.");

    public static string ParameterNotFilterable => Pick(
        "По параметру «{0}» нельзя фильтровать выбранные категории.",
        "The parameter «{0}» cannot filter the selected categories.");

    // --- Итог

    public static string ReportCreated => Pick("Создано видов: {0}", "Views created: {0}");

    public static string ReportUpdated => Pick("Обновлено: {0}", "Views updated: {0}");

    public static string ReportFailed => Pick("Не получилось:", "Failed:");

    public static string ReportFailedLine => Pick("  {0}: {1}", "  {0}: {1}");

    public static string ReportHiddenCategories => Pick(
        "Без параметра системы, скрыты на схемах целиком: {0}",
        "No system parameter, hidden on the views entirely: {0}");

    public static string ReportTemplates => Pick(
        "Каждой схеме назначен свой шаблон «{0} - <система>». Чтобы поменять оформление схем, правьте «{0}» и запустите плагин снова.",
        "Each view got its own template «{0} - <system>». To restyle the views, edit «{0}» and run the plugin again.");

    public static string SettingsNotSaved => Pick("Настройки не сохранены: {0}", "Settings were not saved: {0}");
}
