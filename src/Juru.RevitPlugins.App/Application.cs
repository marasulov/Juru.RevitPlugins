using ClashNavigator.Commands;
using Juru.RevitPlugins.Openings.Commands;
using Juru.RevitPlugins.Schedules.Commands;
using Juru.RevitPlugins.SystemViews.Commands;

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
        private const string NoText = "​";

        public override void OnStartup()
        {
            // Язык ставится до сборки панелей: подписи кнопок Revit забирает в момент создания и
            // потом не перечитывает.
            RibbonText.Use(Application.ControlledApplication.Language);

            CreateClashNavigatorPanel();
            // Закрепляемую панель Ревит регистрирует только при запуске; откроет ли её кнопка
            // навигатора вместо окна - выбирает пользователь в настройках.
            ClashNavigator.NavigatorPane.Register(Application, RibbonText.ClashNavigator);
            CreateOpeningsPanel();
            CreateTextPanel();
            CreateSystemsPanel();
        }

        private void CreateClashNavigatorPanel()
        {
            var panel = Application.CreatePanel("ClashNavigator", TabName);

            panel.AddPushButton<DetectiveClashesCommand>(RibbonText.FindClashes)
                .SetImage(IconsPath + "FindClashesIcon16.png")
                .SetLargeImage(IconsPath + "FindClashesIcon32.png")
                .SetToolTip(RibbonText.FindClashesTip);

            panel.AddPushButton<GetClashesCommand>(RibbonText.ClashNavigator)
                .SetImage(IconsPath + "NavigatorIcon16Small.png")
                .SetLargeImage(IconsPath + "NavigatorIcon32.png")
                .SetToolTip(RibbonText.ClashNavigatorTip);

            panel.AddPushButton<CreateFiltersCommand>(RibbonText.SearchSets)
                .SetImage(IconsPath + "SearchSetsIcon16.png")
                .SetLargeImage(IconsPath + "SearchSetsIcon32.png")
                .SetToolTip(RibbonText.SearchSetsTip);

            panel.AddPushButton<SetSettingsCommand>(RibbonText.ClashSettings)
                .SetImage(IconsPath + "SettingsIcon16.png")
                .SetLargeImage(IconsPath + "SettingsIcon32.png")
                .SetToolTip(RibbonText.ClashSettingsTip);
        }

        /// <summary>
        ///     Задания на отверстия. Кнопки разделены по сторонам процесса: слева от разделителя -
        ///     то, что делает инженер в своём файле (вставил, проверил, отправил), справа - то, что
        ///     делают АР и КР с полученными заданиями. См. <c>docs/OpeningsApi.md</c>.
        /// </summary>
        private void CreateOpeningsPanel()
        {
            var panel = Application.CreatePanel(RibbonText.OpeningsPanel, TabName);

            panel.AddPushButton<PlaceOpeningTasksCommand>(RibbonText.PlaceOpenings)
                .SetImage(IconsPath + "PlaceOpeningsIcon16.png")
                .SetLargeImage(IconsPath + "PlaceOpeningsIcon32.png")
                .SetToolTip(RibbonText.PlaceOpeningsTip);

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
                .SetToolTip(RibbonText.UniteOpeningsTip);

            stackPanel.AddPushButton<ShowOpeningTasksCommand>(NoText)
                .SetImage(IconsPath + "OpeningsNavigatorIcon16.png")
                .SetLargeImage(IconsPath + "OpeningsNavigatorIcon32.png")
                .SetToolTip(RibbonText.OpeningsNavigatorTip);

            stackPanel.AddPushButton<SendOpeningTasksCommand>(NoText)
                .SetImage(IconsPath + "SendOpeningsIcon16.png")
                .SetLargeImage(IconsPath + "SendOpeningsIcon32.png")
                .SetToolTip(RibbonText.SendOpeningsTip);

            panel.AddSeparator();

            // Загрузка и согласование - два шага одной работы в файле АР или КР, и делает их один
            // человек подряд, поэтому они делят одно место на панели и переключаются стрелкой.
            var incomingButton = panel.AddSplitButton("IncomingOpeningTasks", RibbonText.IncomingOpenings);

            incomingButton.AddPushButton<LoadOpeningTasksCommand>(RibbonText.LoadOpenings)
                .SetImage(IconsPath + "LoadOpeningsIcon16.png")
                .SetLargeImage(IconsPath + "LoadOpeningsIcon32.png")
                .SetToolTip(RibbonText.LoadOpeningsTip);

            incomingButton.AddPushButton<ShowIncomingOpeningTasksCommand>(RibbonText.ApproveOpenings)
                .SetImage(IconsPath + "ApproveOpeningsIcon16.png")
                .SetLargeImage(IconsPath + "ApproveOpeningsIcon32.png")
                .SetToolTip(RibbonText.ApproveOpeningsTip);

            panel.AddSeparator();

            panel.AddPushButton<SetOpeningsSettingsCommand>(RibbonText.OpeningsSettings)
                .SetImage(IconsPath + "SettingsIcon16.png")
                .SetLargeImage(IconsPath + "SettingsIcon32.png")
                .SetToolTip(RibbonText.OpeningsSettingsTip);
        }

        /// <summary>
        ///     Перевод текста модели: спецификации, помещения, листы и прочие категории.
        /// </summary>
        private void CreateTextPanel()
        {
            var panel = Application.CreatePanel(RibbonText.TextPanel, TabName);

            panel.AddPushButton<ShowSchedulesWindowCommand>(RibbonText.Translator)
                .SetImage(IconsPath + "TranslateIcon16.png")
                .SetLargeImage(IconsPath + "TranslateIcon32.png")
                .SetToolTip(RibbonText.TranslateTip);

            // Обновление копии стоит отдельной кнопкой, а не в окне перевода: оно идёт по открытому
            // виду, ничего не спрашивает и заканчивается раньше, чем окно успело бы открыться.
            panel.AddPushButton<UpdateScheduleCopyCommand>(RibbonText.UpdateCopy)
                .SetImage(IconsPath + "UpdateCopyIcon16.png")
                .SetLargeImage(IconsPath + "UpdateCopyIcon32.png")
                .SetToolTip(RibbonText.UpdateCopyTip);
        }

        /// <summary>
        ///     Инженерные системы: 3D-схемы по системам из открытого вида.
        /// </summary>
        private void CreateSystemsPanel()
        {
            var panel = Application.CreatePanel(RibbonText.SystemsPanel, TabName);

            panel.AddPushButton<CreateSystemViewsCommand>(RibbonText.SystemViews)
                .SetImage(IconsPath + "SystemViewsIcon16.png")
                .SetLargeImage(IconsPath + "SystemViewsIcon32.png")
                .SetToolTip(RibbonText.SystemViewsTip);
        }
    }
}
