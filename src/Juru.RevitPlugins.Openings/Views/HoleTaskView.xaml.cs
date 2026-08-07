using System.Windows;
using Juru.RevitPlugins.Openings.ViewModels;

namespace Juru.RevitPlugins.Openings.Views
{
    /// <summary>
    /// Логика взаимодействия для HoleTaskView.xaml
    /// </summary>
    public partial class HoleTaskView : Window
    {
        public HoleTaskView(HolesVm holesVm)
        {
            InitializeComponent();

            DataContext = holesVm;

            holesVm.CloseAction ??= new Action(this.Close);
        }
    }


}
