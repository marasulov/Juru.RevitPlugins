using System;

using Autodesk.Revit.UI;

namespace Juru.RevitPlugins.Openings.Services;

/// <summary>
/// Показывает необработанное исключение команды так, чтобы его можно было переслать разработчику.
/// </summary>
/// <remarks>
/// Своё окно ошибки нужно потому, что стандартное окно Revit ("Произошла внутренняя ошибка")
/// не называет ни тип исключения, ни место: по скриншоту от пользователя причину не найти.
/// Здесь же видно тип, сообщение и стек - этого хватает, чтобы понять, на каком вызове упало.
/// </remarks>
public static class CommandErrorReport {
    public static Result Show(string title, Exception exception) {
        var dialog = new TaskDialog(title) {
            MainInstruction = "Команда завершилась ошибкой.",
            MainContent = exception.GetType().Name + ": " + exception.Message
                + Environment.NewLine + Environment.NewLine
                + "Пришлите этот текст разработчику - по нему видно, где именно упало.",
            ExpandedContent = exception.ToString()
        };
        dialog.Show();

        return Result.Failed;
    }
}
