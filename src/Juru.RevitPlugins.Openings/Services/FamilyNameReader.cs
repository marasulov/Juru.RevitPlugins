using Juru.RevitPlugins.Openings.Settings;

namespace Juru.RevitPlugins.Openings.Services
{
    internal class FamilyNameReader
    {
        public FamilyNameReader()
        {
            var settingsReader = new SettingsReader();
            var recFamilyNames = settingsReader.GetFamilyTypes().FamilyTypes.Rectangled.FamilyNames;
            var circledFamilyNames = settingsReader.GetFamilyTypes().FamilyTypes.Circled.FamilyNames;

        }
    }
}
