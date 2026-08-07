using Juru.RevitPlugins.ClashNavigator.Commands;
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
            var panel = Application.CreatePanel("ClashNavigator", "JuruPlugins");

            panel.AddPushButton<GetClashesCommand>("Навигатор\nколлизий")
                .SetImage("/Juru.RevitPlugins.App;component/Resources/Icons/NavigatorIcon16Small.png")
                .SetLargeImage("/Juru.RevitPlugins.App;component/Resources/Icons/NavigatorIcon32.png")
                .SetToolTip("Открывает навигатор коллизий");

            panel.AddPushButton<SetSettingsCommand>("Настройки")
                .SetImage("/Juru.RevitPlugins.App;component/Resources/Icons/SettingsIcon16.png")
                .SetLargeImage("/Juru.RevitPlugins.App;component/Resources/Icons/SettingsIcon32.png")
                .SetToolTip("Настройки навигатора коллизий");
        }
    }
}