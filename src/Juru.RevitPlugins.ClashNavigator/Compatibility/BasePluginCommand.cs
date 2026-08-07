using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using dosymep.Bim4Everyone.ProjectConfigs;
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

        // CreatePlatformServices() builds a ChildKernel whose parent is ServicesProvider.Instance.
        // Instance stays null until something sets it, so without this every ChildKernel gets a
        // null ParentResolutionRoot and later throws deep inside Ninject.Extensions.ChildKernel.
        // We can't call the vendor's ServicesProvider.LoadInstance() for this - it binds
        // IMessageBoxService via dosymep.Xpf.Core.Ninject, which wraps DevExpress.Xpf.Core; that
        // library isn't vendored here (see MSI installer dependency isolation notes - the whole
        // DevExpress tree was previously stripped out as impractical to ship). So we bind the
        // small set of platform services our commands actually resolve from the parent kernel
        // ourselves, using the same WpfUI-based services already used elsewhere in this plugin.
        if(ServicesProvider.Instance is null) {
            var platformKernel = new StandardKernel();
            platformKernel.Bind<IConfigSerializer>().To<ConfigSerializer>().InSingletonScope();
            platformKernel.UseWpfUIMessageBox();
            platformKernel.UseWpfUIProgressDialog();

            // Instance's setter is non-public - LoadInstance() is the only sanctioned way to set it,
            // but it requires DevExpress. Set the backing property directly instead.
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
