using Juru.RevitPlugins.ClashNavigator.ViewModels;

namespace Juru.RevitPlugins.ClashNavigator.Views
{
    public sealed partial class Juru_RevitPlugins_ClashNavigatorView
    {
        public Juru_RevitPlugins_ClashNavigatorView(Juru_RevitPlugins_ClashNavigatorViewModel viewModel)
        {
            DataContext = viewModel;
            InitializeComponent();
        }
    }
}