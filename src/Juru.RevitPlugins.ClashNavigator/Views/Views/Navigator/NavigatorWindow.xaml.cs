using System.Windows;

namespace Juru.RevitPlugins.ClashNavigator.Views.Navigator
{
    // Наследуемся от обычного системного окна
    public partial class NavigatorWindow : Window
    {

        public NavigatorWindow()
        {
            InitializeComponent();
        }

        // Оставили свойства просто как публичные (без override), 
        // на случай если они нужны вашей логике конфигов.
        public string PluginName => "Навигатор коллизий";
        public string ProjectConfigName => nameof(NavigatorWindow);

        private void ButtonCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}