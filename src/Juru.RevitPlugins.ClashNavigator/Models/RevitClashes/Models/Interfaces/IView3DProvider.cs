
using Autodesk.Revit.DB;

namespace Juru.RevitPlugins.ClashNavigator.Models.Interfaces;
internal interface IView3DProvider {
    View3D GetView(Document doc, string name);
}
