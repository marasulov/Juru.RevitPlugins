using System;
using Juru.RevitPlugins.Openings.Contracts;

namespace Juru.RevitPlugins.Openings.ViewModels.Commands
{
    public class ZoomToPointCommand : CommandBase
    {
        private readonly IZoomEntity _intersectionPointZoom;

        public ZoomToPointCommand(IZoomEntity intersectionPointZoom)
        {
            _intersectionPointZoom = intersectionPointZoom;
        }

        public event EventHandler? CanExecuteChanged;

        //public bool CanExecute(object parameter)
        //{
        //    var vm = (HolesVm?)parameter;

        //    return vm?.SelectedEntity is not null;
        //}

        public override void Execute(object parameter)
        {
            var id = Convert.ToInt32(parameter);

            _intersectionPointZoom.Zoom(id);
        }
    }
}
