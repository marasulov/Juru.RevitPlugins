using Juru.RevitPlugins.Openings.Contracts.DTOs;

namespace Juru.RevitPlugins.Openings.Contracts;

public interface IFamilyInsertService
{
    void InsertFamily(AllHolesDto allHoles);
}