using Juru.RevitPlugins.App.Commands;
using Nice3point.Revit.Toolkit.External;

namespace Juru.RevitPlugins.App
{
    /// <summary>
    ///     Application entry point
    /// </summary>
    [UsedImplicitly]
    public class Application : ExternalApplication
    {
        public override void OnStartup()
        {
            CreateRibbon();
        }

        private void CreateRibbon()
        {
            var panel = Application.CreatePanel("Commands", "Juru.RevitPlugins.App");

            panel.AddPushButton<StartupCommand>("Execute")
                .SetImage("/Juru.RevitPlugins.App;component/Resources/Icons/RibbonIcon16.png")
                .SetLargeImage("/Juru.RevitPlugins.App;component/Resources/Icons/RibbonIcon32.png");
        }
    }
}