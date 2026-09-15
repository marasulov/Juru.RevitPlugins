using Autodesk.Revit.ApplicationServices;

namespace Juru.RevitPlugins.Schedules.Localization;

/// <summary>Язык интерфейса плагина.</summary>
public enum PluginLanguage
{
    Russian,
    English
}

/// <summary>
///     Строки интерфейса на двух языках.
/// </summary>
/// <remarks>
///     <para>
///         Не <c>.resx</c>: перевод лежал бы в сателлитной сборке, а её пришлось бы раскладывать
///         подпапкой рядом с плагином в бандле — лишний шаг установки, который легко забыть, и тогда
///         окно молча вернулось бы к одному языку. Здесь оба языка видны рядом, ключей нет вовсе,
///         а значит и опечаток в ключах.
///     </para>
///     <para>
///         Язык берётся из интерфейса Ревита и за сеанс не меняется, поэтому разметка обращается
///         к строкам через <c>x:Static</c>: он разворачивается при разборе XAML, то есть уже после
///         <see cref="Use" />.
///     </para>
/// </remarks>
public static class Text
{
    /// <summary>Текущий язык. Русский по умолчанию: плагин писался под русские модели.</summary>
    public static PluginLanguage Language { get; private set; } = PluginLanguage.Russian;

    /// <summary>
    ///     Ставит язык по интерфейсу Ревита. Вызывается до создания окон: разметка читает строки
    ///     при разборе.
    /// </summary>
    /// <remarks>
    ///     Русский интерфейс Ревита — русский плагин, любой другой — английский: держать перевод на
    ///     все языки Ревита незачем, английский понимают везде.
    /// </remarks>
    public static void Use(LanguageType language)
    {
        Language = language is LanguageType.Russian ? PluginLanguage.Russian : PluginLanguage.English;
    }

    private static string Pick(string russian, string english)
    {
        return Language is PluginLanguage.Russian ? russian : english;
    }

    public static string WindowTitle
    {
        get { return Pick("Английские спецификации", "English schedules"); }
    }

    public static string SheetSchedulesHeader
    {
        get { return Pick("Спецификации на листах", "Sheet schedules"); }
    }

    public static string RedrawHint
    {
        get { return Pick("С галочкой «Перерисовать в линии» копия рисуется заново линиями и текстом: переведены и значения ячеек, но копия статична. Без неё перевод пишется в саму спецификацию - имя, заголовки колонок и фильтры: спецификация остаётся живой, а её значения станут английскими по мере перевода объектов на вкладках категорий.", "With «Redraw as lines» each schedule is redrawn as lines and text: cell values are translated too, but the copy is static. Without it the translation goes into the schedule itself - its name, column headings and filters: the schedule stays live, and its values turn English as the objects on the category tabs get translated."); }
    }

    public static string SelectAll
    {
        get { return Pick("Выбрать все", "Select all"); }
    }

    public static string SelectNone
    {
        get { return Pick("Снять все", "Clear all"); }
    }

    public static string Settings
    {
        get { return Pick("Настройки", "Settings"); }
    }

    public static string SettingsTooltip
    {
        get { return Pick("Служба перевода, ключи, языки и вид записываемого значения", "Translation service, keys, languages and the shape of the written value"); }
    }

    public static string SearchPlaceholder
    {
        get { return Pick("Поиск по имени или листу", "Search by name or sheet"); }
    }

    public static string ScanModel
    {
        get { return Pick("Сканировать модель", "Scan model"); }
    }

    public static string ScanActiveView
    {
        get { return Pick("Сканировать активный вид", "Scan active view"); }
    }

    public static string ScanModelTooltip
    {
        get { return Pick("Искать переводимый текст во всей модели", "Look for translatable text in the whole model"); }
    }

    public static string ScanActiveViewTooltip
    {
        get { return Pick("Искать только в активном виде: на большой модели это заметно быстрее", "Look only in the active view: on a large model that is noticeably faster"); }
    }

    public static string StatusReadingScope
    {
        get { return Pick("Читаю активный вид", "Reading the active view"); }
    }

    public static string Show
    {
        get { return Pick("Показать", "Show"); }
    }

    public static string ShowScheduleTooltip
    {
        get { return Pick("Открыть спецификацию этой строки в Ревите", "Open this schedule in Revit"); }
    }

    public static string ShowElementTooltip
    {
        get { return Pick("Показать элемент выбранной строки в модели", "Show the selected row's element in the model"); }
    }

    public static string TranslateMissing
    {
        get { return Pick("Перевести недостающие", "Translate missing"); }
    }

    public static string Build
    {
        get { return Pick("Создать", "Create"); }
    }

    public static string Close
    {
        get { return Pick("Закрыть", "Close"); }
    }

    public static string Save
    {
        get { return Pick("Сохранить", "Save"); }
    }

    public static string Cancel
    {
        get { return Pick("Отмена", "Cancel"); }
    }

    public static string Check
    {
        get { return Pick("Проверить", "Check"); }
    }

    public static string RedrawAsLines
    {
        get { return Pick("Перерисовать в линии", "Redraw as lines"); }
    }

    public static string TranslationService
    {
        get { return Pick("Служба перевода", "Translation service"); }
    }

    public static string SchedulesTab
    {
        get { return Pick("Спецификации", "Schedules"); }
    }

    public static string TranslationsTab
    {
        get { return Pick("Строки на перевод", "Texts to translate"); }
    }

    public static string ColumnSchedule
    {
        get { return Pick("Спецификация", "Schedule"); }
    }

    public static string ColumnSheets
    {
        get { return Pick("Листы", "Sheets"); }
    }

    public static string ColumnColumns
    {
        get { return Pick("Колонок", "Columns"); }
    }

    public static string ColumnRows
    {
        get { return Pick("Строк", "Rows"); }
    }

    public static string ColumnTranslatable
    {
        get { return Pick("Строк на перевод", "Texts to translate"); }
    }

    public static string ColumnUntranslated
    {
        get { return Pick("Без перевода", "Not translated"); }
    }

    public static string ColumnStatus
    {
        get { return Pick("Состояние", "Status"); }
    }

    public static string ColumnKind
    {
        get { return Pick("Что это", "What it is"); }
    }

    public static string ColumnSourceText
    {
        get { return Pick("Исходный текст", "Source text"); }
    }

    public static string ColumnTranslation
    {
        get { return Pick("Перевод", "Translation"); }
    }

    public static string ColumnResult
    {
        get { return Pick("Что запишется", "Will be written"); }
    }

    public static string ColumnSchedules
    {
        get { return Pick("Спецификации", "Schedules"); }
    }

    public static string ColumnWhere
    {
        get { return Pick("Где", "Where"); }
    }

    public static string AnyStatus
    {
        get { return Pick("Все состояния", "All statuses"); }
    }

    public static string StatusNeedsTranslation
    {
        get { return Pick("нужен перевод", "needs translation"); }
    }

    public static string StatusWillRevert
    {
        get { return Pick("будет откачено", "will be reverted"); }
    }

    public static string StatusTranslatedInModel
    {
        get { return Pick("переведено в модели", "translated in the model"); }
    }

    public static string StatusReadyToWrite
    {
        get { return Pick("к записи", "ready to write"); }
    }

    public static string StatusReadError
    {
        get { return Pick("ошибка чтения", "read failed"); }
    }

    public static string StatusNothingToTranslate
    {
        get { return Pick("переводить нечего", "nothing to translate"); }
    }

    public static string StatusTranslated
    {
        get { return Pick("переведена", "translated"); }
    }

    public static string StatusNeedsTranslationCount
    {
        get { return Pick("нужен перевод", "needs translation"); }
    }

    public static string KindTitle
    {
        get { return Pick("название", "name"); }
    }

    public static string KindColumnHeading
    {
        get { return Pick("заголовок колонки", "column heading"); }
    }

    public static string KindCellValue
    {
        get { return Pick("значение", "cell value"); }
    }

    public static string CategoryRooms
    {
        get { return Pick("Помещения", "Rooms"); }
    }

    public static string CategoryAreas
    {
        get { return Pick("Зоны", "Areas"); }
    }

    public static string CategorySheets
    {
        get { return Pick("Листы", "Sheets"); }
    }

    public static string CategoryViews
    {
        get { return Pick("Виды", "Views"); }
    }

    public static string CategoryTextNotes
    {
        get { return Pick("Текстовые примечания", "Text notes"); }
    }

    public static string CategoryLevels
    {
        get { return Pick("Уровни", "Levels"); }
    }

    public static string CategoryMaterials
    {
        get { return Pick("Материалы", "Materials"); }
    }

    public static string CategoryRevisions
    {
        get { return Pick("Ревизии", "Revisions"); }
    }

    public static string CategoryDimensions
    {
        get { return Pick("Размеры", "Dimensions"); }
    }

    public static string CategoryFamilyTypes
    {
        get { return Pick("Семейства", "Family types"); }
    }

    public static string CategoryKeynotes
    {
        get { return Pick("Ключевые примечания", "Keynotes"); }
    }

    public static string CategoryGlobalParameters
    {
        get { return Pick("Параметры", "Parameters"); }
    }

    public static string CategoryScheduleTexts
    {
        get { return Pick("Текст спецификаций", "Schedule texts"); }
    }

    public static string EmptyRooms
    {
        get { return Pick("В модели нет помещений с русским текстом — переводить нечего.", "The model has no rooms with source-language text."); }
    }

    public static string EmptyAreas
    {
        get { return Pick("В модели нет зон с русским текстом — переводить нечего.", "The model has no areas with source-language text."); }
    }

    public static string EmptySheets
    {
        get { return Pick("В модели нет листов с русскими названиями.", "The model has no sheets with source-language names."); }
    }

    public static string EmptyViews
    {
        get { return Pick("В модели нет видов с русскими названиями.", "The model has no views with source-language names."); }
    }

    public static string EmptyTextNotes
    {
        get { return Pick("В модели нет текстовых примечаний с русским текстом.", "The model has no text notes with source-language text."); }
    }

    public static string EmptyLevels
    {
        get { return Pick("В модели нет уровней с русскими названиями.", "The model has no levels with source-language names."); }
    }

    public static string EmptyMaterials
    {
        get { return Pick("В модели нет материалов с русским текстом.", "The model has no materials with source-language text."); }
    }

    public static string EmptyRevisions
    {
        get { return Pick("В модели нет ревизий с русским текстом.", "The model has no revisions with source-language text."); }
    }

    public static string EmptyDimensions
    {
        get { return Pick("В модели нет размеров с русскими надписями.", "The model has no dimensions with source-language overrides."); }
    }

    public static string EmptyFamilyTypes
    {
        get { return Pick("В модели нет расставленных типоразмеров с русскими названиями.", "The model has no placed family types with source-language names."); }
    }

    public static string EmptyGlobalParameters
    {
        get { return Pick("В модели нет параметров элементов с русским текстом.", "The model has no element parameters with source-language text."); }
    }

    public static string EmptyKeynotes
    {
        get { return Pick("Таблица ключевых примечаний не назначена, недоступна или в ней нет русского текста. Путь к таблице задаётся в «Аннотации — Ключевое примечание — Настройка».", "The keynote table is not assigned, is unavailable, or has no source-language text. Its path is set in Annotate — Keynote — Keynoting Settings."); }
    }

    public static string EmptyScheduleTexts
    {
        get { return Pick("В спецификациях нет русского текста в имени, заголовках колонок и фильтрах.", "Schedules have no source-language text in their names, column headings or filters."); }
    }

    public static string TargetName
    {
        get { return Pick("Имя", "Name"); }
    }

    public static string TargetFamilyName
    {
        get { return Pick("Имя семейства", "Family name"); }
    }

    public static string TargetNoteText
    {
        get { return Pick("Текст", "Text"); }
    }

    public static string TargetDepartment
    {
        get { return Pick("Отдел", "Department"); }
    }

    public static string TargetOccupancy
    {
        get { return Pick("Назначение", "Occupancy"); }
    }

    public static string TargetFinishFloor
    {
        get { return Pick("Отделка пола", "Floor finish"); }
    }

    public static string TargetFinishWall
    {
        get { return Pick("Отделка стен", "Wall finish"); }
    }

    public static string TargetFinishCeiling
    {
        get { return Pick("Отделка потолка", "Ceiling finish"); }
    }

    public static string TargetFinishBase
    {
        get { return Pick("Плинтус", "Base finish"); }
    }

    public static string TargetComments
    {
        get { return Pick("Комментарии", "Comments"); }
    }

    public static string TargetSheetName
    {
        get { return Pick("Имя листа", "Sheet name"); }
    }

    public static string TargetViewName
    {
        get { return Pick("Имя вида", "View name"); }
    }

    public static string TargetDescription
    {
        get { return Pick("Описание", "Description"); }
    }

    public static string TargetTypeName
    {
        get { return Pick("Имя типоразмера", "Type name"); }
    }

    public static string TargetMark
    {
        get { return Pick("Марка", "Mark"); }
    }

    public static string TargetScheduleName
    {
        get { return Pick("Имя спецификации", "Schedule name"); }
    }

    public static string TargetHeadingPrefix
    {
        get { return Pick("Заголовок", "Heading"); }
    }

    public static string TargetFilterPrefix
    {
        get { return Pick("Фильтр", "Filter"); }
    }

    public static string TargetSegment
    {
        get { return Pick("сегмент", "segment"); }
    }

    public static string TargetAbove
    {
        get { return Pick("Надпись сверху", "Text above"); }
    }

    public static string TargetBelow
    {
        get { return Pick("Надпись снизу", "Text below"); }
    }

    public static string TargetPrefix
    {
        get { return Pick("Префикс", "Prefix"); }
    }

    public static string TargetSuffix
    {
        get { return Pick("Суффикс", "Suffix"); }
    }

    public static string TargetValueOverride
    {
        get { return Pick("Замена значения", "Value override"); }
    }

    public static string TargetIssuedTo
    {
        get { return Pick("Кому выдано", "Issued to"); }
    }

    public static string TargetIssuedBy
    {
        get { return Pick("Кем выдано", "Issued by"); }
    }

    public static string TargetGlobalParameter
    {
        get { return Pick("Элемент", "Element"); }
    }

    public static string ContextLevel
    {
        get { return Pick("Уровень", "Level"); }
    }

    public static string ContextMaterial
    {
        get { return Pick("Материал", "Material"); }
    }

    public static string ContextAreaScheme
    {
        get { return Pick("Схема зонирования", "Area scheme"); }
    }

    public static string ContextOutsideView
    {
        get { return Pick("вне вида", "not in a view"); }
    }

    public static string StatusReadingSchedules
    {
        get { return Pick("Читаю спецификации с листов…", "Reading sheet schedules…"); }
    }

    public static string StatusBuilding
    {
        get { return Pick("Собираю английский комплект…", "Building the English set…"); }
    }

    public static string StatusReadFailed
    {
        get { return Pick("Прочитать спецификации не удалось.", "Could not read the schedules."); }
    }

    public static string StatusBuildFailed
    {
        get { return Pick("Сборка не удалась.", "The build failed."); }
    }

    public static string StatusNoSchedules
    {
        get { return Pick("На листах нет ни одной спецификации.", "There are no schedules on sheets."); }
    }

    public static string StatusNothingChecked
    {
        get { return Pick("Ничего не отмечено: отметьте спецификации или текст на вкладках категорий.", "Nothing is checked: check schedules or text on the category tabs."); }
    }

    public static string StatusPickScheduleRow
    {
        get { return Pick("Выберите строку со спецификацией, которую нужно открыть.", "Select the row with the schedule you want to open."); }
    }

    public static string StatusPickElementRow
    {
        get { return Pick("Выберите строку, которую нужно показать в модели.", "Select the row you want to show in the model."); }
    }

    public static string StatusSettingsSaved
    {
        get { return Pick("Настройки сохранены.", "Settings saved."); }
    }

    public static string NoteNothingToTranslate
    {
        get { return Pick("Пустых переводов среди показанных строк нет.", "None of the shown rows is missing a translation."); }
    }

    public static string NoteServiceNotConfigured
    {
        get { return Pick("Служба перевода не настроена.", "The translation service is not configured."); }
    }

    public static string SettingsTitle
    {
        get { return Pick("Настройки перевода", "Translation settings"); }
    }

    public static string SettingsFromLanguage
    {
        get { return Pick("С языка", "From"); }
    }

    public static string SettingsToLanguage
    {
        get { return Pick("на язык", "to"); }
    }

    public static string SettingsBatchSize
    {
        get { return Pick("Строк в запросе", "Texts per request"); }
    }

    public static string SettingsLanguageHint
    {
        get { return Pick("Код языка на цель влияет и на имя файла ключевых примечаний: keynotes_EN.txt.", "The target language code also names the keynote file: keynotes_EN.txt."); }
    }

    public static string SettingsVariable
    {
        get { return Pick("Переменная", "Variable"); }
    }

    public static string SettingsNewKey
    {
        get { return Pick("Новый ключ", "New key"); }
    }

    public static string SettingsKeySet
    {
        get { return Pick("ключ задан", "key is set"); }
    }

    public static string SettingsKeyMissing
    {
        get { return Pick("ключ не задан", "key is not set"); }
    }

    public static string SettingsVariableMissing
    {
        get { return Pick("имя переменной не задано", "variable name is not set"); }
    }

    public static string SettingsDeepLHint
    {
        get { return Pick("Бесплатный ключ DeepL заканчивается на :fx — по нему выбирается бесплатный адрес службы.", "A free DeepL key ends with :fx — that is how the free endpoint is chosen."); }
    }

    public static string SettingsHelp
    {
        get { return Pick("Как получить ключ?", "How do I get a key?"); }
    }

    public static string SettingsDeepLHelpTitle
    {
        get { return Pick("Как получить ключ DeepL", "How to get a DeepL key"); }
    }

    public static string SettingsDeepLHelpBody
    {
        get
        {
            return Pick(
                """
                1. Зайдите на deepl.com/pro-api и нажмите «Sign up for free» (тариф DeepL API Free — 500 000 символов в месяц бесплатно).
                2. Зарегистрируйтесь. Для бесплатного тарифа тоже просят привязать карту, но без превышения лимита списаний не будет.
                3. После подтверждения почты откройте Account → API keys — там будет готовый ключ вида xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx:fx.
                4. Скопируйте ключ целиком, вместе с «:fx» на конце — по нему плагин узнаёт, что ключ бесплатный, и обращается на правильный адрес.
                5. Вставьте ключ в поле «Новый ключ» ниже и нажмите «Проверить», затем «Сохранить».
                """,
                """
                1. Go to deepl.com/pro-api and click "Sign up for free" (the DeepL API Free plan gives 500,000 characters per month).
                2. Register. The free plan also asks for a card, but you won't be charged unless you go over the limit.
                3. After confirming your email, open Account → API keys — a ready-made key like xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx:fx will be there.
                4. Copy the whole key, including the ":fx" at the end — that is how the plugin recognizes a free key and calls the right endpoint.
                5. Paste the key into the "New key" field below, click "Check", then "Save".
                """);
        }
    }

    public static string SettingsGoogleHelpTitle
    {
        get { return Pick("Как получить ключ Google Cloud Translation", "How to get a Google Cloud Translation key"); }
    }

    public static string SettingsGoogleHelpBody
    {
        get
        {
            return Pick(
                """
                1. Зайдите на console.cloud.google.com и войдите с Google-аккаунтом.
                2. Создайте новый проект (или выберите существующий) вверху страницы.
                3. В меню слева откройте APIs & Services → Library, найдите «Cloud Translation API» и нажмите «Enable».
                4. Подключите биллинг: Billing → Link a billing account (без карты ключ не заработает; у API есть бесплатный лимит символов в месяц).
                5. Откройте APIs & Services → Credentials → Create Credentials → API key и скопируйте появившийся ключ.
                6. Рекомендуется ограничить ключ: в его настройках включите API restrictions → Restrict key → только «Cloud Translation API».
                7. Вставьте ключ в поле «Новый ключ» ниже и нажмите «Проверить», затем «Сохранить».
                """,
                """
                1. Go to console.cloud.google.com and sign in with a Google account.
                2. Create a new project (or pick an existing one) at the top of the page.
                3. In the left menu open APIs & Services → Library, find "Cloud Translation API" and click "Enable".
                4. Link a billing account: Billing → Link a billing account (the key won't work without one; the API also has a monthly free character quota).
                5. Open APIs & Services → Credentials → Create Credentials → API key and copy the key.
                6. Recommended: restrict the key — in its settings, turn on API restrictions → Restrict key → "Cloud Translation API" only.
                7. Paste the key into the "New key" field below, click "Check", then "Save".
                """);
        }
    }

    public static string SettingsWrittenValue
    {
        get { return Pick("Что записывается в модель", "What goes into the model"); }
    }

    public static string SettingsKeepSource
    {
        get { return Pick("Сохранять и оригинал, и перевод", "Keep both the original and the translation"); }
    }

    public static string SettingsSeparator
    {
        get { return Pick("Разделитель", "Separator"); }
    }

    public static string SettingsKeepSourceHint
    {
        get { return Pick("Двуязычное значение вида «Кабинет / Office»: один и тот же параметр читается и в русском комплекте, и в английском.", "A bilingual value like «Кабинет / Office»: the same parameter reads in both the source and the English set."); }
    }

    public static string SummarySchedules
    {
        get { return Pick("Выбрано {0} из {1}, строк без перевода {2}", "Checked {0} of {1}, texts without a translation {2}"); }
    }

    public static string SummaryTranslations
    {
        get { return Pick("Строк {0}, без перевода {1}", "Texts {0}, without a translation {1}"); }
    }

    public static string SummaryCategory
    {
        get { return Pick("Строк {0}, отмечено {1}, без перевода {2}", "Rows {0}, checked {1}, without a translation {2}"); }
    }

    public static string ReportDoneSchedules
    {
        get { return Pick("Спецификаций {0}", "Schedules {0}"); }
    }

    public static string ReportWithErrors
    {
        get { return Pick(", с ошибками {0}", ", with errors {0}"); }
    }

    public static string ReportDone
    {
        get { return Pick("Готово. {0}В модели записано {1}, откачено {2}.{3}", "Done. {0}Written to the model {1}, reverted {2}.{3}"); }
    }

    public static string ReportSkipped
    {
        get { return Pick(" Отмечено без перевода и пропущено: {0}.", " Checked without a translation and skipped: {0}."); }
    }

    public static string ReportSkippedLong
    {
        get { return Pick("Отмечено, но перевода нет ни в окне, ни в глоссарии: {0}. Такие строки пропущены — переведите их кнопкой «Перевести недостающие» или впишите вручную.", "Checked, but no translation in the window or the glossary: {0}. Such rows are skipped - translate them with «Translate missing» or type them in."); }
    }

    public static string ReportService
    {
        get { return Pick("Служба перевода: {0}, переведено сейчас {1}", "Translation service: {0}, translated now {1}"); }
    }

    public static string ReportGlossaryUntranslated
    {
        get { return Pick("Строк глоссария без перевода: {0}", "Glossary texts without a translation: {0}"); }
    }

    public static string ReportCategoryLine
    {
        get { return Pick("{0}: записано {1}, откачено {2}, без изменений {3}", "{0}: written {1}, reverted {2}, unchanged {3}"); }
    }

    public static string ReportNoCategories
    {
        get { return Pick("Категории модели не переводились.", "No model categories were translated."); }
    }

    public static string ReportTranslationErrors
    {
        get { return Pick("Ошибок перевода: {0}. {1}", "Translation errors: {0}. {1}"); }
    }

    public static string ReportOpen
    {
        get { return Pick("Открыть отчёт", "Open the report"); }
    }

    public static string NoteTranslated
    {
        get { return Pick("Переведено {0} из {1} ({2}).", "Translated {0} of {1} ({2})."); }
    }

    public static string NoteTranslatedWithErrors
    {
        get { return Pick("Переведено {0} из {1}. Ошибок: {2}. {3}", "Translated {0} of {1}. Errors: {2}. {3}"); }
    }

    public static string NoteTranslatorEmpty
    {
        get { return Pick("{0} ответил пусто.", "{0} returned nothing."); }
    }

    public static string NoteChecking
    {
        get { return Pick("Проверяю {0}…", "Checking {0}…"); }
    }

    public static string NoteNotOnThisTab
    {
        get { return Pick("На этой вкладке переводить нечего: перевод спецификаций идёт по кнопке «Создать».", "Nothing to translate on this tab: schedules are translated by «Create»."); }
    }

    public static string NoteShowNotOnThisTab
    {
        get { return Pick("На этой вкладке показывать нечего: строка здесь - текст, а не объект модели.", "Nothing to show on this tab: a row here is a text, not a model object."); }
    }

    public static string FailureCannotWrite
    {
        get { return Pick("{0}: «{1}» записать нельзя", "{0}: cannot write «{1}»"); }
    }

    public static string FailureCannotRevert
    {
        get { return Pick("{0}: «{1}» откатить нельзя", "{0}: cannot revert «{1}»"); }
    }

    public static string FailureElementDeleted
    {
        get { return Pick("{0}: элемент удалён из модели", "{0}: the element was deleted from the model"); }
    }

    public static string SettingsSaveFailed
    {
        get { return Pick("Настройки применены, но сохранить их не удалось: {0}", "Settings applied, but could not be saved: {0}"); }
    }
}
