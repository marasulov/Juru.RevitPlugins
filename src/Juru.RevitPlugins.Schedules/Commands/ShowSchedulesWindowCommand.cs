using System.Collections.Generic;
using System.Linq;
using System.Windows.Interop;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

using Juru.RevitPlugins.Schedules.Services;
using Juru.RevitPlugins.Schedules.ViewModels;
using Juru.RevitPlugins.Schedules.Views;

using Nice3point.Revit.Toolkit;
using Nice3point.Revit.Toolkit.External;

namespace Juru.RevitPlugins.Schedules.Commands;

/// <summary>
///     Показывает выбор спецификаций для английского комплекта. Спецификации читаются до открытия окна:
///     дальше окно работает по снимкам и Revit API не трогает.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public sealed class ShowSchedulesWindowCommand : ExternalCommand
{
    public override void Execute()
    {
        var uiDocument = Application.ActiveUIDocument;

        using var dialogSuppression = RevitContext.BeginDialogSuppressionScope(TaskDialogResult.Ok);

        var config = SchedulesConfig.Load();
        var glossary = GlossaryFile.Read(config.ResolveGlossaryPath());

        var schedules = new SheetScheduleCopyService(uiDocument).ReadAll(null);

        if (schedules.Count is 0)
        {
            TaskDialog.Show("Английские спецификации", "На листах нет ни одной спецификации.");
            return;
        }

        var rows = BuildRows(schedules, glossary);
        var translations = TranslationRowViewModel.Build(schedules, glossary);

        var window = new SchedulesWindow(
            new SchedulesWindowViewModel(rows, translations, config.Translation));

        ShowModal(window);
    }

    private static IReadOnlyList<ScheduleRowViewModel> BuildRows(
        IReadOnlyList<ReadSchedule> schedules,
        IReadOnlyDictionary<string, string> glossary)
    {
        return schedules
            .Select(schedule => new ScheduleRowViewModel(schedule, glossary))
            .ToList();
    }

    /// <summary>
    ///     Окно принадлежит Ревиту: без этого оно теряется за главным окном и не закрывается вместе с ним.
    /// </summary>
    private void ShowModal(SchedulesWindow window)
    {
        new WindowInteropHelper(window) { Owner = Application.MainWindowHandle };

        window.ShowDialog();
    }
}
