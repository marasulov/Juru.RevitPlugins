using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using dosymep.Bim4Everyone.SimpleServices;
using dosymep.SimpleServices;
using dosymep.WpfUI.Core;

using Juru.RevitPlugins.ClashNavigator.ViewModels.SearchSet;

namespace Juru.RevitPlugins.ClashNavigator.Views.Filters
{
    /// <summary>
    /// Просмотр элементов поискового набора. Открывается из окна наборов и закрывается вместе с ним.
    /// </summary>
    public partial class SearchSetPreviewWindow : WpfUIPlatformWindow
    {
        public SearchSetPreviewWindow(
            ILoggerService loggerService,
            ISerializationService serializationService,
            ILanguageService languageService, ILocalizationService localizationService,
            IUIThemeService uiThemeService, IUIThemeUpdaterService themeUpdaterService)
            : base(loggerService, serializationService, languageService, localizationService,
                uiThemeService, themeUpdaterService)
        {
            InitializeComponent();

            // Заголовки переводятся кодом: DataGridColumn не входит в дерево элементов.
            ColumnLocalization.Apply(ElementsGrid, LocalizationService);

            Loaded += Window_Loaded;
            Closed += Window_Closed;
        }

        public override string PluginName => "Поисковые наборы";
        public override string ProjectConfigName => nameof(SearchSetPreviewWindow);

        private SearchSetPreviewViewModel ViewModel => DataContext as SearchSetPreviewViewModel;

        // Колонки параметров и первый показ набора в Ревите - на Loaded: DataContext окну
        // назначают уже после конструктора.
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (ViewModel is not { } viewModel)
            {
                return;
            }

            for (int i = 0; i < viewModel.ParameterNames.Count; i++)
            {
                ElementsGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = viewModel.ParameterNames[i],
                    Binding = new System.Windows.Data.Binding($"{nameof(SearchSetElementViewModel.Values)}[{i}]"),
                    Width = new DataGridLength(1, DataGridLengthUnitType.Star)
                });
            }

            viewModel.ShowSetCommand.Execute(null);
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            ViewModel?.Close();
        }

        private void ElementsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Только двойной щелчок по строке, а не по заголовку или полосе прокрутки.
            if (e.OriginalSource is DependencyObject source
                && ItemsControl.ContainerFromElement(ElementsGrid, source) is DataGridRow
                && ViewModel?.ShowElementCommand.CanExecute(null) == true)
            {
                ViewModel.ShowElementCommand.Execute(null);
            }
        }

        private void ButtonClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
