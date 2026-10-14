using System.Linq;
using System.Text;
using System.Windows.Interop;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Juru.RevitPlugins.Core.Theming;
using Juru.RevitPlugins.SystemViews.Localization;
using Juru.RevitPlugins.SystemViews.Models;
using Juru.RevitPlugins.SystemViews.Services;
using Juru.RevitPlugins.SystemViews.ViewModels;
using Juru.RevitPlugins.SystemViews.Views;

using Nice3point.Revit.Toolkit.External;

namespace Juru.RevitPlugins.SystemViews.Commands;

/// <summary>
///     Делает 3D-схемы систем из открытого 3D-вида: по виду на систему, с фильтром, который
///     оставляет видимой только её.
/// </summary>
/// <remarks>
///     Меняет модель: создаёт виды и фильтры вида, а у существующих схем с тем же именем
///     переписывает фильтр и оформление. Весь запуск отменяется в Ревите одним шагом.
/// </remarks>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public sealed class CreateSystemViewsCommand : ExternalCommand
{
    public override void Execute()
    {
        Text.Use(Application.Application.Language);

        var document = Application.ActiveUIDocument.Document;

        if (document.ActiveView is not View3D { IsTemplate: false } sourceView)
        {
            TaskDialog.Show(Text.WindowTitle, Text.NeedView3D);
            return;
        }

        var content = new SourceViewContent(document, sourceView);

        if (content.ElementCount is 0)
        {
            TaskDialog.Show(Text.WindowTitle, Text.NoMepElements);
            return;
        }

        JuruTheme.Apply();

        var viewModel = new SystemViewsViewModel(content, SystemViewsSettings.Load());
        var window = new SystemViewsWindow(viewModel);
        _ = new WindowInteropHelper(window) { Owner = Application.MainWindowHandle };

        if (window.ShowDialog() is not true || viewModel.Request is not { } request)
        {
            return;
        }

        var settingsError = viewModel.ToSettings().TrySave();

        var report = new SystemViewBuilder(document, content).Build(request);

        TaskDialog.Show(Text.WindowTitle, Describe(report, settingsError));
    }

    private static string Describe(SystemViewsReport report, string? settingsError)
    {
        var text = new StringBuilder();

        text.AppendLine(string.Format(Text.ReportCreated, report.Outcomes.Count(outcome => outcome.Succeeded && outcome.Created)));
        text.AppendLine(string.Format(Text.ReportUpdated, report.Outcomes.Count(outcome => outcome.Succeeded && !outcome.Created)));

        var failed = report.Outcomes.Where(outcome => !outcome.Succeeded).ToList();

        if (failed.Count > 0)
        {
            text.AppendLine().AppendLine(Text.ReportFailed);

            foreach (var outcome in failed)
            {
                text.AppendLine(string.Format(Text.ReportFailedLine, outcome.System, outcome.Error));
            }
        }

        if (report.HiddenCategories.Count > 0)
        {
            text.AppendLine().AppendLine(string.Format(Text.ReportHiddenCategories, string.Join(", ", report.HiddenCategories)));
        }

        if (report.BaseTemplate is { } template)
        {
            text.AppendLine().AppendLine(string.Format(Text.ReportTemplates, template));
        }

        if (settingsError is not null)
        {
            text.AppendLine().AppendLine(string.Format(Text.SettingsNotSaved, settingsError));
        }

        return text.ToString().TrimEnd();
    }
}
