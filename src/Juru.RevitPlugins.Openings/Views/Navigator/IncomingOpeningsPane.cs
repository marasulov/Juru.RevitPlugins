using System;
using System.Windows;

using Autodesk.Revit.UI;

using Juru.RevitPlugins.Core.Theming;
using Juru.RevitPlugins.Openings.Services;

using Nice3point.Revit.Toolkit.Decorators;

namespace Juru.RevitPlugins.Openings.Views.Navigator;

/// <summary>
/// Закреплённая панель Revit с согласованием заданий на отверстия.
/// </summary>
/// <remarks>
/// Регистрация обязана идти из <c>OnStartup</c> - закрепляемую панель Revit позже не принимает.
/// А вот содержимое создаётся не тогда, а когда Revit первый раз показывает панель: элементы WPF,
/// созданные на старте, до <see cref="JuruTheme.Apply"/>, оставляли без оформления кнопки и в этой
/// панели, и в остальных окнах плагина. Поэтому и регистрация обёрнута в try: сорваться на старте
/// она не должна - без ленты не останется ни одна команда.
/// </remarks>
public static class IncomingOpeningsPane {
    /// <summary>
    /// Идентификатор панели. Постоянный: Revit запоминает по нему положение и размер, выбранные
    /// пользователем, - новый GUID на каждый запуск сбрасывал бы их.
    /// </summary>
    public static readonly DockablePaneId PaneId = new(new Guid("6f1e2c74-0a3d-4a1f-9c62-2b7e5d8f4a11"));

    public const string Title = "Согласование заданий на отверстия";

    private static IncomingOpeningsPaneView _view;
    private static object _dataContext;
    private static ExternalEvent _loadEvent;

    /// <summary>
    /// Панель не зарегистрировалась - почему. <c>null</c>, если всё в порядке.
    /// </summary>
    public static string RegistrationError { get; private set; }

    /// <summary>
    /// Регистрирует панель. Вызывается только из <c>OnStartup</c> приложения.
    /// </summary>
    public static void Register(UIControlledApplication application) {
        if(application is null) {
            throw new ArgumentNullException(nameof(application));
        }

        try {
            // Внешнее событие заводится здесь же: ExternalEvent.Create требует контекста API,
            // а OnStartup им является. Через него панель грузит задания по кнопке "Обновить",
            // когда её ещё нечем обновлять - модели представления у неё пока нет.
            _loadEvent = ExternalEvent.Create(new LoadHandler());

            DockablePaneProvider
                .Register(application, PaneId, Title)
                .SetConfiguration(data => {
                    // Именно Creator, а не готовый FrameworkElement: Revit спрашивает провайдера
                    // уже на регистрации, то есть на старте, а создавать элементы WPF тогда нельзя -
                    // созданные до JuruTheme.Apply, они оставляли без оформления кнопки и здесь,
                    // и в остальных окнах плагина. Creator Revit зовёт, когда панель реально
                    // понадобилась.
                    data.FrameworkElementCreator = new ViewCreator();

                    // Справа, как свойства и диспетчер видов: список и панель решения стоят друг
                    // под другом, то есть панели нужна высота, а не ширина. Ширину пользователь
                    // растянет сам, и Revit её запомнит.
                    data.InitialState = new DockablePaneState {
                        DockPosition = DockPosition.Right,
                        MinimumWidth = 620,
                        MinimumHeight = 420
                    };
                });
        } catch(Exception exception) {
            RegistrationError = exception.Message;
        }
    }

    /// <summary>
    /// Подставляет панели модель представления. Работает и до того, как Revit создал содержимое:
    /// значение запоминается и попадает в него при создании.
    /// </summary>
    public static void Attach(object dataContext) {
        _dataContext = dataContext;
        if(_view is not null) {
            _view.DataContext = dataContext;
        }
    }

    /// <summary>
    /// Загружает задания в панель - только по кнопке "Обновить".
    /// </summary>
    /// <remarks>
    /// Сама, при показе панели, загрузка не идёт и идти не должна: она обращается к серверу и ставит
    /// задания в модель. Панель, которую Revit открыл при запуске, успевала полезть в модель, пока
    /// он ещё открывал вид, и файл после этого не открывался вовсе - "не удалось прочитать
    /// содержимое вида". Поэтому загрузку начинает только явное нажатие.
    /// </remarks>
    public static void RequestLoad() {
        _loadEvent?.Raise();
    }

    /// <summary>
    /// Модель представления, которую панель показывает сейчас, - команде ленты нужно понять,
    /// собрана ли панель уже по этому файлу.
    /// </summary>
    public static object DataContext => _dataContext;

    /// <summary>
    /// Собирает панель по активному файлу в контексте API. Причину отказа показываем: нажатие
    /// на "Обновить" - явное действие, и молчать в ответ на него нельзя.
    /// </summary>
    private sealed class LoadHandler : IExternalEventHandler {
        public void Execute(UIApplication uiApplication) {
            string error = IncomingOpeningsPaneLoader.Load(uiApplication);
            if(error is not null) {
                TaskDialog.Show("Согласование заданий", error);
            }
        }

        public string GetName() {
            return "Juru.Openings.IncomingOpeningsPaneLoad";
        }
    }

    /// <summary>
    /// Создаёт содержимое панели по запросу Revit.
    /// </summary>
    /// <remarks>
    /// Каждый раз новое, а не один раз созданное и запомненное: Revit спрашивает содержимое заново
    /// всякий раз, когда пересобирает панель, и тот же самый элемент WPF пытался встать вторым
    /// родителем в чужое дерево. Элемент уже в дереве второй раз не вставляется, и падает это не
    /// исключением в плагине, а Revit целиком.
    /// </remarks>
    private sealed class ViewCreator : IFrameworkElementCreator {
        public FrameworkElement CreateFrameworkElement() {
            return CreateView();
        }
    }

    private static IncomingOpeningsPaneView CreateView() {
        // Тему применяем до создания элементов: WPF-UI раскрашивает то, что создано после Apply.
        JuruTheme.Apply();
        _view = new IncomingOpeningsPaneView { DataContext = _dataContext };
        return _view;
    }
}
