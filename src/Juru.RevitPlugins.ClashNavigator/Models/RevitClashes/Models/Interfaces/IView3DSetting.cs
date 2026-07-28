
using Autodesk.Revit.DB;

namespace Juru.RevitPlugins.ClashNavigator.Models.Interfaces;
internal interface IView3DSetting {
    void Apply(View3D view3D);
}
