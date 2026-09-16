using Juru.RevitPlugins.ClashNavigator.Commands;
using Juru.RevitPlugins.Openings.Commands;
using Juru.RevitPlugins.Schedules.Commands;

using Nice3point.Revit.Toolkit.External;

namespace Juru.RevitPlugins.App
{
    /// <summary>
    ///     Application entry point
    /// </summary>
    [UsedImplicitly]
    public class Application : ExternalApplication
    {
        private const string TabName = "JuruPlugins";
        private const string IconsPath = "/Juru.RevitPlugins.App;component/Resources/Icons/";

        /// <summary>
        ///     Подпись кнопки, у которой подписи быть не должно. Revit отбивает и пустой текст,
        ///     и пробел: <c>ButtonData.Text</c> проверяет значение после обрезки пробелов и падает
        ///     с <c>ArgumentException: The value cannot be empty</c>. U+200B (пробел нулевой
        ///     ширины) пробелом не считается, обрезку проходит и ничего не рисует.
        /// </summary>
        private const string NoText = "\u200b";

        public override void OnStartup()
        {
            CreateClashNavigatorPanel();
            CreateOpeningsPanel();
            CreateSchedulesPanel();
        }

        private void CreateClashNavigatorPanel()
        {
            var panel = Application.CreatePanel("ClashNavigator", TabName);

            panel.AddPushButton<FindClashesCommand>("Поиск\nколлизий")
                .SetImage(IconsPath + "FindClashesIcon16.png")
                .SetLargeImage(IconsPath + "FindClashesIcon32.png")
                .SetToolTip("Ищет коллизии в этом файле и связях по настроенным проверкам");

            panel.AddPushButton<GetClashesCommand>("Навигатор\nколлизий")
                .SetImage(IconsPath + "NavigatorIcon16Small.png")
                .SetLargeImage(IconsPath + "NavigatorIcon32.png")
                .SetToolTip("Открывает навигатор коллизий");

            panel.AddPushButton<SetFiltersCommand>("Поисковые\nнаборы")
                .SetImage(IconsPath + "SearchSetsIcon16.png")
                .SetLargeImage(IconsPath + "SearchSetsIcon32.png")
                .SetToolTip("Настраивает наборы, которыми проверки набирают элементы для сравнения");

            panel.AddPushButton<SetSettingsCommand>("Настройки")
                .SetImage(IconsPath + "SettingsIcon16.png")
                .SetLargeImage(IconsPath + "SettingsIcon32.png")
                .SetToolTip("Настройки навигатора коллизий");
        }

        /// <summary>
        ///     Задания на отверстия. Кнопки разделены по сторонам процесса: слева от разделителя -
        ///     то, что делает инженер в своём файле (вставил, проверил, отправил), справа - то, что
        ///     делают АР и КР с полученными заданиями. См. <c>docs/OpeningsApi.md</c>.
        /// </summary>
        private void CreateOpeningsPanel()
        {
            var panel = Application.CreatePanel("Отверстия", TabName);

            panel.AddPushButton<PlaceOpeningTasksCommand>("Вставить\nзадания")
                .SetImage(IconsPath + "PlaceOpeningsIcon16.png")
                .SetLargeImage(IconsPath + "PlaceOpeningsIcon32.png")
                .SetToolTip("Расставляет задания на отверстия по пересечениям инженерных "
                            + "элементов с конструкциями из связей");

            // Склейка, навигатор и отправка - шаги вокруг той же вставки, поэтому стоят мелкими
            // друг над другом, а не наравне с ней. Подписи убраны, чтобы кнопки не растягивали
            // панель: что делает кнопка, говорит подсказка при наведении. Больше трёх в столбец
            // не ставить - четвёртая уедет в новый столбец.
            //
            // Заголовок подсказки Revit берёт из подписи кнопки, а её тут нет, поэтому имя команды
            // стоит первой строкой самой подсказки - иначе при наведении видно только описание.
            var stackPanel = panel.AddStackPanel();

            stackPanel.AddPushButton<UniteOpeningTasksCommand>(NoText)
                .SetImage(IconsPath + "UniteOpeningsIcon16.png")
                .SetLargeImage(IconsPath + "UniteOpeningsIcon32.png")
                .SetToolTip("Объединить задания\n"
                            + "Объединяет выбранные задания на отверстия в одно");

            stackPanel.AddPushButton<ShowOpeningTasksCommand>(NoText)
                .SetImage(IconsPath + "OpeningsNavigatorIcon16.png")
                .SetLargeImage(IconsPath + "OpeningsNavigatorIcon32.png")
                .SetToolTip("Навигатор заданий\n"
                            + "Открывает навигатор по заданиям, выданным из этого файла");

            stackPanel.AddPushButton<SendOpeningTasksCommand>(NoText)
                .SetImage(IconsPath + "SendOpeningsIcon16.png")
                .SetLargeImage(IconsPath + "SendOpeningsIcon32.png")
                .SetToolTip("Отправить задания\n"
                            + "Отправляет задания этого файла на сервер согласования");

            panel.AddSeparator();

            // Загрузка и согласование - два шага одной работы в файле АР или КР, и делает их один
            // человек подряд, поэтому они делят одно место на панели и переключаются стрелкой.
            var incomingButton = panel.AddSplitButton("IncomingOpeningTasks", "Задания от инженеров");

            incomingButton.AddPushButton<LoadOpeningTasksCommand>("Загрузить\nзадания")
                .SetImage(IconsPath + "LoadOpeningsIcon16.png")
                .SetLargeImage(IconsPath + "LoadOpeningsIcon32.png")
                .SetToolTip("Загружает задания на отверстия с сервера в файл АР или КР");

            incomingButton.AddPushButton<ShowIncomingOpeningTasksCommand>("Согласование\nзаданий")
                .SetImage(IconsPath + "ApproveOpeningsIcon16.png")
                .SetLargeImage(IconsPath + "ApproveOpeningsIcon32.png")
                .SetToolTip("Открывает навигатор согласования полученных заданий");

            panel.AddSeparator();

            panel.AddPushButton<SetOpeningsSettingsCommand>("Настройки")
                .SetImage(IconsPath + "SettingsIcon16.png")
                .SetLargeImage(IconsPath + "SettingsIcon32.png")
                .SetToolTip("Настройки выдачи заданий на отверстия");
        }

        /// <summary>
        ///     Английский комплект: перевод спецификаций, помещений, листов и прочих категорий модели.
        /// </summary>
        private void CreateSchedulesPanel()
        {
            var panel = Application.CreatePanel("Спецификации", TabName);

            panel.AddPushButton<ShowSchedulesWindowCommand>("Английский\nкомплект")
                .SetImage(IconsPath + "EnglishSchedulesIcon16.png")
                .SetLargeImage(IconsPath + "EnglishSchedulesIcon32.png")
                .SetToolTip("Переводит спецификации, помещения, листы и другие категории модели на английский");
        }
    }
}
