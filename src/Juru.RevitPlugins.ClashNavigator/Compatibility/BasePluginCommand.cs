using System;
using System.Collections.Generic;
using System.Windows;

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

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
