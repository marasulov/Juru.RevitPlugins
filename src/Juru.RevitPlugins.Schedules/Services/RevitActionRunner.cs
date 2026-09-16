using System;

using Autodesk.Revit.UI;

namespace Juru.RevitPlugins.Schedules.Services;

/// <summary>
///     Выполняет работу с моделью по запросу немодального окна.
/// </summary>
/// <remarks>
///     Немодальное окно живёт вне контекста Revit API, и обращаться к модели прямо из его команд
///     нельзя. Поэтому работа заворачивается во внешнее событие, которое Ревит выполняет в своём
///     потоке, когда освободится. Создавать его нужно из команды: <see cref="ExternalEvent.Create" />
///     требует контекста API. Тот же приём, что у навигатора заданий на отверстия — там он же нужен,
///     чтобы открыть вид и выделить элемент.
/// </remarks>
public sealed class RevitActionRunner : IExternalEventHandler
{
    private readonly ExternalEvent externalEvent;

    private Action<UIApplication>? action;

    public RevitActionRunner()
    {
        externalEvent = ExternalEvent.Create(this);
    }

    /// <summary>Просит Ревит выполнить действие в контексте API.</summary>
    public void Run(Action<UIApplication> action)
    {
        this.action = action ?? throw new ArgumentNullException(nameof(action));

        externalEvent.Raise();
    }

    public void Execute(UIApplication uiApplication)
    {
        var pending = action;
        action = null;

        if (pending is null)
        {
            return;
        }

        try
        {
            pending(uiApplication);
        }
        catch (Exception exception)
        {
            // Исключение всплывает вне стека команды: без своего сообщения Ревит покажет только
            // собственную ошибку без причины.
#if DEBUG
            TaskDialog.Show(Localization.Text.WindowTitle, $"{exception.Message}\n\n{CrashLog.Write(exception)}");
#else
            TaskDialog.Show(Localization.Text.WindowTitle, exception.Message);
#endif
        }
    }

    public string GetName()
    {
        return "Juru.Schedules.RevitActionRunner";
    }
}
