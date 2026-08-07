using System.Collections.Generic;

namespace Juru.RevitPlugins.Openings.Settings
{
    public class SettingsRoot
    {
        public HoleFamilyTypes FamilyTypes { get; set; }
        public List<string> FamilyNames { get; set; }
    }
    public class Circled
    {
        public FamilyNames FamilyNames { get; set; }
        public FamilyNames ArFamilyNames { get; set; }
        public List<string> FamilyParameters { get; set; }
    }

    public class FamilyNames
    {
        public string WallType { get; set; }
        public string FloorType { get; set; }
    }

    public class FamilyTypes
    {
        public Circled Circled { get; set; }
        public Rectangled Rectangled { get; set; }
    }

    public class Rectangled
    {
        public FamilyNames FamilyNames { get; set; }
        public FamilyNames ArFamilyNames { get; set; }
        public List<string> FamilyParameters { get; set; }
    }

    public class HoleFamilyTypes
    {
        public FamilyTypes FamilyTypes { get; set; }
    }

    public class HoleFamilyNames
    {
        public List<string> FamilyNames { get; set; }
    }

}
