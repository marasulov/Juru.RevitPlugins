using System;

using Autodesk.Revit.UI;

using Juru.RevitPlugins.Core.Theming;

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
            DockablePaneProvider
                .Register(application, PaneId, Title)
                .SetConfiguration(data => {
                    data.FrameworkElement = GetOrCreateView();

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
    /// Модель представления, которую панель показывает сейчас, - команде ленты нужно понять,
    /// собрана ли панель уже по этому файлу.
    /// </summary>
    public static object DataContext => _dataContext;

    private static IncomingOpeningsPaneView GetOrCreateView() {
        if(_view is not null) {
            return _view;
        }

        // Тему применяем до создания элементов, а не после: WPF-UI раскрашивает то, что создаётся
        // после Apply, и панель, собранная раньше, оставалась серой.
        JuruTheme.Apply();
        _view = new IncomingOpeningsPaneView { DataContext = _dataContext };
        return _view;
    }
}
