using System.Collections.Generic;

namespace Juru.RevitPlugins.Openings.Contracts
{
    public interface ISettingsReader
    {
        List<string> GetFamilyNames();
        
        Dictionary<string, MappingInfo> GetArMappings();
    }
}
