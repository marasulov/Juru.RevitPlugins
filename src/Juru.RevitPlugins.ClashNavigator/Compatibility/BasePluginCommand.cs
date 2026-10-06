using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using dosymep.Bim4Everyone.SimpleServices;
using dosymep.Serializers;
using dosymep.WpfUI.Core.Ninject;

using Ninject;

namespace dosymep.Bim4Everyone;

/// <summary>
/// Command base local to this plugin. The copied Core base requests global
/// platform services before a command can create its Ninject kernel; this
/// project does not register that global logger and every command therefore
/// fails before its own Execute method begins.
/// </summary>
public abstract class BasePluginCommand : IExternalCommand {
    protected BasePluginCommand() => PluginName = GetType().Name;

    public bool FromGui { get; private set; } = true;
    protected IDictionary<string, string> JournalData { get; private set; }
    protected string PluginName { get; set; }

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) {
        JournalData = commandData?.JournalData;
        FromGui = true;
        if(JournalData is not null
           && JournalData.TryGetValue("ExecutedFromUI", out var fromGui)
           && bool.TryParse(fromGui, out var isFromGui)) {
            FromGui = isFromGui;
        }

        // The plugin itself no longer resolves anything from the static ServicesProvider: these
        // two services cannot even be built there (WpfUI ones need an IHasTheme), so error
        // messages and search progress now use their own windows. The instance is still set in
        // case dosymep code reaches for it internally; the vendor's LoadInstance() is avoided
        // because it binds through dosymep.Xpf.Core.Ninject, i.e. DevExpress, not vendored here.
        if(ServicesProvider.Instance is null) {
            var platformKernel = new StandardKernel();
            platformKernel.UseWpfUIMessageBox();
            platformKernel.UseWpfUIProgressDialog();

            // Instance has a non-public setter and LoadInstance() is its only sanctioned entry point.
            typeof(ServicesProvider)
                .GetProperty(nameof(ServicesProvider.Instance), BindingFlags.Public | BindingFlags.Static)
                .SetValue(null, platformKernel);
        }

        try {
            Execute(commandData.Application);
            return Result.Succeeded;
        } catch(OperationCanceledException) {
            return Result.Cancelled;
        } catch(Autodesk.Revit.Exceptions.OperationCanceledException) {
            return Result.Cancelled;
        } catch(Exception exception) {
            message = exception.Message;
            TaskDialog.Show(PluginName, exception.ToString());
            return Result.Failed;
        }
    }

    protected abstract void Execute(UIApplication uiApplication);

    protected void Notification(Window window) {
        if(window.ShowDialog() == false) throw new OperationCanceledException();
    }
}
