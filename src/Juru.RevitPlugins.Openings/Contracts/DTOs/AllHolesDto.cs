using System.Collections.Generic;

namespace Juru.RevitPlugins.Openings.Contracts.DTOs
{
    public class AllHolesDto
    {
        public AllHolesDto(List<NewHolesDto> newFamiliesDtos, List<ActualHoleModelDto> actualFamiliesDtos, List<OutdatedFamilyDto> outdatedFamiliesDtos)
        {
            NewFamiliesDtos = newFamiliesDtos;
            ActualFamiliesDtos = actualFamiliesDtos;
            OutdatedFamiliesDtos = outdatedFamiliesDtos;
        }

        public List<NewHolesDto> NewFamiliesDtos { get; set; }
        public List<ActualHoleModelDto> ActualFamiliesDtos { get; set; }
        public List<OutdatedFamilyDto> OutdatedFamiliesDtos { get; set; }
    }
}
