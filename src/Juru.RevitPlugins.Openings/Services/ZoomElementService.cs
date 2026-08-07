using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Juru.RevitPlugins.Openings.Contracts;

namespace Juru.RevitPlugins.Openings.Services
{
    public class ZoomElementService : IZoomEntity
    {
        private UIDocument _uiDocument;

        public ZoomElementService(UIDocument uiDocument)
        {
            _uiDocument = uiDocument;
        }

        public void Zoom(int id)
        {
            _uiDocument?.ShowElements(new ElementId(id));
        }
    }
}
