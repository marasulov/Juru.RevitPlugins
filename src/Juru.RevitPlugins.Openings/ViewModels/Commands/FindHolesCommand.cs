using System;
using Juru.RevitPlugins.Openings.Contracts;
using Juru.RevitPlugins.Openings.Contracts.DTOs;

namespace Juru.RevitPlugins.Openings.ViewModels.Commands
{
    public class FindHolesCommand : CommandBase
    {
        private readonly IFindHoleService _findHoleService;

        public FindHolesCommand(IFindHoleService findHoleService)
        {
            _findHoleService = findHoleService;
        }

        public event EventHandler? CanExecuteChanged;

        public event Action<AllHolesDto> ResultObtained;

        public override void Execute(object parameter)
        {
            var res = _findHoleService.FindHoles();

            ResultObtained?.Invoke(res);
        }

    }
}
