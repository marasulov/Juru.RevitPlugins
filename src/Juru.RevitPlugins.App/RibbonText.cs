using Autodesk.Revit.ApplicationServices;

namespace Juru.RevitPlugins.App
{
    /// <summary>
    ///     Подписи и подсказки кнопок ленты на двух языках.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Лента строится один раз при запуске Ревита, до того как открыто хоть одно окно
    ///         плагина, поэтому взять строки из словарей окон нельзя: <c>PluginLocalization</c>
    ///         грузит их в ресурсы конкретного окна, а окна на этот момент ещё нет. Здесь тот же
    ///         подход, что в <c>Schedules.Localization.Text</c> - оба языка рядом, без ключей,
    ///         а значит и без опечаток в ключах.
    ///     </para>
    ///     <para>
    ///         Язык берётся у Ревита и за сеанс не меняется: сменить его можно только перезапуском,
    ///         а лента переживает любое число открытых моделей.
    ///     </para>
    /// </remarks>
    internal static class RibbonText
    {
        private static bool _russian = true;

        /// <summary>
        ///     Ставит язык по интерфейсу Ревита. Русский интерфейс - русская лента, любой другой -
        ///     английская: держать перевод на все языки Ревита незачем, английский понимают везде.
        /// </summary>
        public static void Use(LanguageType language)
        {
            _russian = language == LanguageType.Russian;
        }

        private static string Pick(string russian, string english)
        {
            return _russian ? russian : english;
        }

        // --- Панели

        public static string OpeningsPanel => Pick("Отверстия", "Openings");

        public static string TextPanel => Pick("Текст", "Text");

        // --- Поиск коллизий

        public static string FindClashes => Pick("Поиск\nколлизий", "Find\nclashes");

        public static string FindClashesTip => Pick(
            "Ищет коллизии в этом файле и связях по настроенным проверкам",
            "Looks for clashes in this file and its links, using the configured checks");

        public static string ClashNavigator => Pick("Навигатор\nколлизий", "Clash\nnavigator");

        public static string ClashNavigatorTip => Pick(
            "Открывает навигатор коллизий",
            "Opens the clash navigator");

        public static string SearchSets => Pick("Поисковые\nнаборы", "Search\nsets");

        public static string SearchSetsTip => Pick(
            "Настраивает наборы, которыми проверки набирают элементы для сравнения",
            "Configures the sets from which the checks take elements to compare");

        public static string ClashSettings => Pick("Настройки", "Settings");

        public static string ClashSettingsTip => Pick(
            "Настройки навигатора коллизий",
            "Clash navigator settings");

        // --- Отверстия

        public static string PlaceOpenings => Pick("Вставить\nзадания", "Place\ntasks");

        public static string PlaceOpeningsTip => Pick(
            "Расставляет задания на отверстия по пересечениям инженерных элементов с конструкциями из связей",
            "Places opening tasks where MEP elements cross the structures in the linked models");

        public static string UniteOpeningsTip => Pick(
            "Объединить задания\nОбъединяет выбранные задания на отверстия в одно",
            "Unite tasks\nMerges the selected opening tasks into one");

        public static string OpeningsNavigatorTip => Pick(
            "Навигатор заданий\nОткрывает навигатор по заданиям, выданным из этого файла",
            "Task navigator\nOpens the navigator over the tasks issued from this file");

        public static string SendOpeningsTip => Pick(
            "Отправить задания\nОтправляет задания этого файла на сервер согласования",
            "Send tasks\nSends this file's tasks to the approval server");

        public static string IncomingOpenings => Pick("Задания от инженеров", "Tasks from engineers");

        public static string LoadOpenings => Pick("Загрузить\nзадания", "Load\ntasks");

        public static string LoadOpeningsTip => Pick(
            "Загружает задания на отверстия с сервера в файл АР или КР",
            "Loads opening tasks from the server into the architectural or structural file");

        public static string ApproveOpenings => Pick("Согласование\nзаданий", "Approve\ntasks");

        public static string ApproveOpeningsTip => Pick(
            "Открывает навигатор согласования полученных заданий",
            "Opens the approval navigator over the received tasks");

        public static string OpeningsSettings => Pick("Настройки", "Settings");

        public static string OpeningsSettingsTip => Pick(
            "Настройки выдачи заданий на отверстия",
            "Settings for issuing opening tasks");

        // --- Текст

        /// <summary>Имя плагина перевода: одно на оба языка, как и всякое имя.</summary>
        public static string Translator => "Translator";

        public static string TranslateTip => Pick(
            "Переводит спецификации, помещения, листы и другие категории модели на английский",
            "Translates schedules, rooms, sheets and other model categories into English");

        public static string UpdateCopy => Pick("Обновить\nкопии", "Update\ncopies");

        public static string UpdateCopyTip => Pick(
            "Пересобирает все переведённые копии модели по их оригиналам",
            "Rebuilds every translated copy in the model from its original");
    }
}
