using System.ComponentModel;

namespace Juru.RevitPlugins.ClashNavigator.Resources;

internal interface IWindowClosingHandler {
    void OnWindowClosing(CancelEventArgs e);
}
