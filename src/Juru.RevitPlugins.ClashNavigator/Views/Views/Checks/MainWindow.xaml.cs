using System.Windows;

using dosymep.Bim4Everyone.SimpleServices;
using dosymep.SimpleServices;
using dosymep.WpfUI.Core;

using Juru.RevitPlugins.ClashNavigator.Resources;

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
            CheckBoxBrushes.OverrideCheckedStates(this);
            // Заголовки колонок переводятся кодом: DataGridColumn не входит в дерево элементов,
            // и me:LocalizationSource в его Header оставался пустым.
            Juru.RevitPlugins.ClashNavigator.Views.ColumnLocalization.Apply(ChecksGrid, localizationService);

            // DataContext назначает ядро свойством уже после конструктора.
            DataContextChanged += (_, e) => {
                if(e.OldValue is ViewModels.ClashDetective.MainViewModel oldViewModel) {
                    oldViewModel.CloseRequested -= ViewModelCloseRequested;
                }
                if(e.NewValue is ViewModels.ClashDetective.MainViewModel newViewModel) {
                    newViewModel.CloseRequested += ViewModelCloseRequested;
                }
            };
        }

        public override string PluginName => "Поиск коллизий";
        public override string ProjectConfigName => nameof(MainWindow);

        // Кнопка «Показать коллизии» в строке проверки открывает навигатор сама, командой строки;
        // окну проверок при этом делать нечего - оно остаётся открытым.
        private void ButtonOk_Click(object sender, RoutedEventArgs e)
        {
        }

        private void ViewModelCloseRequested(object sender, System.EventArgs e)
        {
            Close();
        }

        private void ButtonCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}