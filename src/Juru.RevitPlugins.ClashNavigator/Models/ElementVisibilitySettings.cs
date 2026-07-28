
using System.Windows.Media;

namespace Juru.RevitPlugins.ClashNavigator.Models;
internal class ElementVisibilitySettings {
    public System.Windows.Media.Color Color { get; set; } = System.Windows.Media.Color.FromRgb(255, 255, 0);

    public int Transparency { get; set; } = 40;
}
