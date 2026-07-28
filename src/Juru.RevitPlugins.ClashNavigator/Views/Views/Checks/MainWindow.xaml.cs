using System.Windows;

namespace Juru.RevitPlugins.ClashNavigator.Views.Checks
{

    // Наследуемся от стандартного Window
    public partial class MainWindow : Window
    {

        public MainWindow()
        {
            InitializeComponent();
        }

        // Если здесь были override PluginName и т.д., просто удалите слово override
        public string PluginName => "Поиск коллизий";
        public string ProjectConfigName => nameof(MainWindow);

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