using System.Windows.Interop;

using Autodesk.Revit.Attributes;

using Juru.RevitPlugins.ProjectSetup.Views;

using Nice3point.Revit.Toolkit.External;

namespace Juru.RevitPlugins.ProjectSetup.Commands;

/// <summary>
///     Окно подготовки проекта: проект и объекты, запуск этапов А и Б. Папки и права создаёт
///     агент ClashTracker на сервере, файлы — это окно. Кнопки на ленте нет — Add-in Manager.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public sealed class ProjectSetupCommand : ExternalCommand
{
    public override void Execute()
    {
        var window = new ProjectSetupWindow(Application.Application);
        new WindowInteropHelper(window).Owner = Application.MainWindowHandle;
        window.ShowDialog();

        // «Открыть генплан»: пока было открыто модальное окно, Revit не дал бы активировать документ
        if (window.DocumentToOpen != null)
            Application.OpenAndActivateDocument(window.DocumentToOpen);
    }
}
