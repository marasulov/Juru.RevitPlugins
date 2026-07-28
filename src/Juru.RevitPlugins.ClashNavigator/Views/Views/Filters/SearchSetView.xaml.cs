using System.Windows;

namespace Juru.RevitPlugins.ClashNavigator.Views.Filters
{ // Проверьте, чтобы namespace совпадал

    // Наследуемся от стандартного Window!
    public partial class SearchSetView : Window
    {

        public SearchSetView()
        {
            InitializeComponent();
        }

        public string PluginName => nameof(Juru.RevitPlugins.ClashNavigator);
        public string ProjectConfigName => nameof(SearchSetView);
    }
}