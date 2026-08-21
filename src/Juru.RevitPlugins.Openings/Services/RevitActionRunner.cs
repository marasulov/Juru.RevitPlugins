using System;

using Autodesk.Revit.UI;

namespace Juru.RevitPlugins.Openings.Services;

/// <summary>
/// Выполняет действия с моделью по запросу немодального окна.
/// </summary>
/// <remarks>
/// Немодальное окно живёт вне контекста Revit API, и обращаться к модели напрямую из его команд
/// нельзя: чтение геометрии и выделение элементов оттуда падает. Поэтому работа с моделью
/// заворачивается во внешнее событие, которое Revit выполняет в своём потоке, когда освободится.
/// Создавать его нужно из команды: <see cref="ExternalEvent.Create"/> требует контекста API.
/// </remarks>
public sealed class RevitActionRunner : IExternalEventHandler {
    private readonly ExternalEvent _externalEvent;
    private Action<UIApplication> _action;

    public RevitActionRunner() {
        _externalEvent = ExternalEvent.Create(this);
    }

    /// <summary>
    /// Просит Revit выполнить действие в контексте API.
    /// </summary>
    public void Run(Action<UIApplication> action) {
        _action = action ?? throw new ArgumentNullException(nameof(action));
        _externalEvent.Raise();
    }

    public void Execute(UIApplication uiApplication) {
        var action = _action;
        _action = null;
        if(action is null) {
            return;
        }

        try {
            action(uiApplication);
        } catch(Exception exception) {
            // Исключение здесь всплывает вне стека команды, и без своего сообщения Revit покажет
            // только собственную ошибку без причины.
            TaskDialog.Show("Задания на отверстия", exception.Message);
        }
    }

    public string GetName() {
        return "Juru.Openings.RevitActionRunner";
    }
}
