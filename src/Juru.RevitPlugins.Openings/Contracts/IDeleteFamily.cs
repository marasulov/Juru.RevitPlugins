using Juru.RevitPlugins.Openings.Contracts.DTOs;

namespace Juru.RevitPlugins.Openings.Contracts;

public interface IDeleteFamily
{
    public void Delete(OutdatedFamilyDto dto);
}