using Autodesk.Revit.DB;

namespace Juru.RevitPlugins.ClashNavigator.Models.Interfaces;
internal interface IRevitRuleCreator {
    FilterRule Create(StorageType storageType, ElementId paramId, object value);
}
