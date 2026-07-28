
using Autodesk.Revit.DB;

using Juru.RevitPlugins.ClashNavigator.Models.Interfaces;

namespace Juru.RevitPlugins.ClashNavigator.Models.RevitViewSettings;
internal class DisplayStyleSetting : IView3DSetting {
    private readonly DisplayStyle _style;

    public DisplayStyleSetting(DisplayStyle style) {
        _style = style;
    }

    public void Apply(View3D view3D) {
        if(view3D.CanModifyDisplayStyle()) {
            view3D.DisplayStyle = _style;
        }
    }
}
