using Juru.RevitPlugins.Openings.ViewModels;

namespace Juru.RevitPlugins.Openings.Views
{
    public sealed partial class Juru_RevitPlugins_OpeningsView
    {
        public Juru_RevitPlugins_OpeningsView(Juru_RevitPlugins_OpeningsViewModel viewModel)
        {
            DataContext = viewModel;
            InitializeComponent();
        }
    }
}