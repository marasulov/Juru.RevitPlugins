using System;
using System.Collections.Generic;
using Juru.RevitPlugins.Openings.Contracts.DTOs;
using Juru.RevitPlugins.Openings.ViewModels.Base;
using Juru.RevitPlugins.Openings.ViewModels.Commands;

namespace Juru.RevitPlugins.Openings.ViewModels
{
    public class HolesVm : BaseViewModel
    {
        private List<ActualHoleModelDto> _actualHoles;
        private List<OutdatedFamilyDto> _outdatedHoles;
        private List<NewHolesDto> _newHoles;

        private bool _canExecute = true;
        private string[] _holeFigureTypes;
        private bool _isAllActualHoleSelected;
        private bool _isAllNewHoleSelected = true;
        private bool _isAllOutdatedHoleSelected;

        public HolesVm(
            FindHolesCommand findHolesCommand,
            CreateHolesCommand createHolesCommand,
            ZoomToPointCommand zoomToPointCommand)
        {
            FindHolesCommand = findHolesCommand;
            findHolesCommand.ResultObtained += FindHolesCommand_ResultObtained;
            CreateHolesCommand = createHolesCommand;
            ZoomToPointCommand = zoomToPointCommand;
            HoleFigureTypes = new[] { "Square", "Circle" };
        }


        public bool IsAllNewHoleSelected
        {
            get => _isAllNewHoleSelected;
            set
            {
                _isAllNewHoleSelected = value;
                OnPropertyChanged();
                NewHoles.ForEach(x => x.IsInsert = IsAllNewHoleSelected);
            }
        }

        public bool IsAllActualHoleSelected
        {
            get => _isAllActualHoleSelected;
            set
            {
                _isAllActualHoleSelected = value;
                OnPropertyChanged();
                ActualHoles.ForEach(x => x.IsDelete = IsAllActualHoleSelected);
            }
        } 

        public bool IsAllOutdatedHoleSelected
        {
            get => _isAllOutdatedHoleSelected;
            set
            {
                _isAllOutdatedHoleSelected = value;
                OnPropertyChanged();
                OutdatedHoles.ForEach(x => x.IsDelete = IsAllOutdatedHoleSelected);
            }
        }

        private void FindHolesCommand_ResultObtained(AllHolesDto obj)
        {
            ActualHoles = obj.ActualFamiliesDtos;
            NewHoles = obj.NewFamiliesDtos;
            OutdatedHoles = obj.OutdatedFamiliesDtos;
        }

        public string[] HoleFigureTypes
        {
            get => _holeFigureTypes;
            set => Set(ref _holeFigureTypes, value);
        }

        public List<NewHolesDto> NewHoles
        {
            get => _newHoles;
            set => Set(ref _newHoles, value);
        }

        public List<ActualHoleModelDto> ActualHoles
        {
            get => _actualHoles;
            set => Set(ref _actualHoles, value);
        }

        public List<OutdatedFamilyDto> OutdatedHoles
        {
            get => _outdatedHoles;
            set => Set(ref _outdatedHoles, value);
        }

        public Action CloseAction { get; set; }

        public FindHolesCommand FindHolesCommand { get; }

        public CreateHolesCommand CreateHolesCommand { get; }

        public ZoomToPointCommand ZoomToPointCommand { get; }
    }
}
