using System.Windows;

using dosymep.Bim4Everyone.SimpleServices;
using dosymep.SimpleServices;
using dosymep.WpfUI.Core;

namespace Juru.RevitPlugins.ClashNavigator.Views.Checks
{
    public partial class MainWindow : WpfUIPlatformWindow
    {
        public MainWindow(
            ILoggerService loggerService,
            ISerializationService serializationService,
            ILanguageService languageService, ILocalizationService localizationService,
            IUIThemeService uiThemeService, IUIThemeUpdaterService themeUpdaterService)
            : base(loggerService, serializationService, languageService, localizationService,
                uiThemeService, themeUpdaterService)
        {
            InitializeComponent();
        }

        public override string PluginName => "Поиск коллизий";
        public override string ProjectConfigName => nameof(MainWindow);

        // Эти методы оставляем
        private void ButtonOk_Click(object sender, RoutedEventArgs e)
        {
            // ... логика, которая здесь была ...
        }

        private void ButtonCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}