using System;
using System.ComponentModel;
using System.Windows;

using dosymep.Bim4Everyone.SimpleServices;
using dosymep.SimpleServices;
using dosymep.WpfUI.Core;

using Juru.RevitPlugins.ClashNavigator.ViewModels.SearchSet;

namespace Juru.RevitPlugins.ClashNavigator.Views.Filters
{
    public partial class FiltersWindow : WpfUIPlatformWindow
    {
        private SearchSetPreviewWindow _previewWindow;

        public FiltersWindow(
            ILoggerService loggerService,
            ISerializationService serializationService,
            ILanguageService languageService, ILocalizationService localizationService,
            IUIThemeService uiThemeService, IUIThemeUpdaterService themeUpdaterService)
            : base(loggerService, serializationService, languageService, localizationService,
                uiThemeService, themeUpdaterService)
        {
            InitializeComponent();

            // Подписка кодом, а не из разметки: модель представления окну назначают уже после
            // конструктора (WithPropertyValue в SearchSetsWindowFactory).
            DataContextChanged += Window_DataContextChanged;
            Closing += Window_Closing;
        }

        public override string PluginName => "Поисковые наборы";
        public override string ProjectConfigName => nameof(FiltersWindow);

        private void Window_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is FiltersViewModel oldViewModel)
            {
                oldViewModel.CloseRequested -= ViewModel_CloseRequested;
                oldViewModel.AskFilterName = null;
                oldViewModel.ShowPreview = null;
            }

            if (e.NewValue is FiltersViewModel viewModel)
            {
                viewModel.CloseRequested += ViewModel_CloseRequested;
                viewModel.AskFilterName = AskFilterName;
                viewModel.ShowPreview = ShowPreview;
            }
        }

        // ОК сохраняет наборы и просит закрыть окно - закрытие идёт следом за состоявшимся
        // сохранением, поэтому вопрос о несохранённых изменениях здесь уже не возникает.
        private void ViewModel_CloseRequested(object sender, EventArgs e)
        {
            Close();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (DataContext is FiltersViewModel viewModel && !viewModel.ConfirmClose())
            {
                e.Cancel = true;
            }
        }

        private void ButtonCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private bool AskFilterName(FilterNameViewModel viewModel)
        {
            return new FilterNameWindow(viewModel, LocalizationService, this).ShowDialog() == true;
        }

        // Окно просмотра одно: новый просмотр заменяет прежний, а не копит окна. Владелец - окно
        // наборов: просмотр держится поверх него и закрывается вместе с ним.
        private void ShowPreview(SearchSetPreviewViewModel viewModel)
        {
            _previewWindow?.Close();
            var window = new SearchSetPreviewWindow(
                LoggerService, SerializationService, LanguageService, LocalizationService,
                UIThemeService, ThemeUpdaterService)
            {
                DataContext = viewModel,
                Owner = this
            };
            window.Closed += (_, _) =>
            {
                if (_previewWindow == window)
                {
                    _previewWindow = null;
                }
            };
            _previewWindow = window;
            window.Show();
        }
    }
}
