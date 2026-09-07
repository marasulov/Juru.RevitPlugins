using System.Windows;

using dosymep.Bim4Everyone.SimpleServices;
using dosymep.SimpleServices;
using dosymep.WpfUI.Core;

namespace Juru.RevitPlugins.ClashNavigator.Views.Filters
{
    public partial class FiltersWindow : WpfUIPlatformWindow
    {
        public FiltersWindow(
            ILoggerService loggerService,
            ISerializationService serializationService,
            ILanguageService languageService, ILocalizationService localizationService,
            IUIThemeService uiThemeService, IUIThemeUpdaterService themeUpdaterService)
            : base(loggerService, serializationService, languageService, localizationService,
                uiThemeService, themeUpdaterService)
        {
            InitializeComponent();
        }

        public override string PluginName => "Поисковые наборы";
        public override string ProjectConfigName => nameof(FiltersWindow);

        // ОК сохраняет наборы и закрывает окно. Кнопка выключена, пока сохранять нельзя,
        // поэтому закрытие здесь всегда идёт следом за состоявшимся сохранением.
        private void ButtonOk_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ButtonCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
