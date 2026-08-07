using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using dosymep.Bim4Everyone.SimpleServices;
using dosymep.SimpleServices;
using dosymep.WpfUI.Core;

namespace Juru.RevitPlugins.ClashNavigator.Views.Navigator
{
    public partial class NavigatorWindow : WpfUIPlatformWindow
    {
        private Window _imagePreviewWindow;
        private Image _imagePreviewImage;

        public NavigatorWindow(
            ILoggerService loggerService,
            ISerializationService serializationService,
            ILanguageService languageService, ILocalizationService localizationService,
            IUIThemeService uiThemeService, IUIThemeUpdaterService themeUpdaterService)
            : base(loggerService, serializationService, languageService, localizationService,
                uiThemeService, themeUpdaterService)
        {
            InitializeComponent();
        }

        // Used by the legacy ClashDetective.CheckViewModel.ShowClashes() path, which constructs this
        // window directly instead of through the Ninject kernel. Theme/localization services stay
        // unavailable on this path (same as before WpfUIPlatformWindow was introduced).
        public NavigatorWindow()
        {
            InitializeComponent();
        }

        public override string PluginName => "Навигатор коллизий";
        public override string ProjectConfigName => nameof(NavigatorWindow);

        private void ButtonCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ClashImage_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Image image || image.Source is null)
            {
                return;
            }

            if (_imagePreviewWindow is null)
            {
                _imagePreviewImage = new Image
                {
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(10)
                };
                _imagePreviewWindow = new Window
                {
                    Title = "Снимок коллизии",
                    Content = _imagePreviewImage,
                    Width = 900,
                    Height = 700,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Owner = this
                };
                _imagePreviewWindow.Closed += (_, _) => _imagePreviewWindow = null;
            }

            _imagePreviewImage.Source = image.Source;

            if (_imagePreviewWindow.IsVisible)
            {
                _imagePreviewWindow.Activate();
            }
            else
            {
                _imagePreviewWindow.Show();
            }
        }
    }
}
