using System.Collections.Generic;

using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExternalService;
using Autodesk.Revit.UI;

namespace Juru.RevitPlugins.ClashNavigator.Services.ClashHighlight;
/// <summary>
/// Включает и снимает цветную подсветку элементов коллизии.
/// </summary>
/// <remarks>
/// Все методы вызывать только в контексте Revit API: регистрация сервера в службе и
/// перерисовка вида требуют его. Сервер один на процесс и регистрируется при первой
/// подсветке; снимается вместе с подсветкой, когда закрывают навигатор, - чтобы после
/// закрытия плагина в Ревите не оставалось его серверов отрисовки.
/// </remarks>
internal static class ClashHighlighter {
    /// <summary>
    /// Цвет первого элемента коллизии.
    /// </summary>
    public static readonly Color FirstColor = new(229, 57, 53);

    /// <summary>
    /// Цвет второго элемента коллизии.
    /// </summary>
    /// <remarks>
    /// Не синий: элементы коллизии ещё и выделены, а выделение Ревит рисует синим поверх, -
    /// синий элемент на нём не отличить от выделенного.
    /// </remarks>
    public static readonly Color SecondColor = new(255, 179, 0);

    private static ClashHighlightServer _server;

    /// <summary>
    /// Показывает элементы на виде цветом; прежняя подсветка заменяется.
    /// </summary>
    public static void Show(UIDocument uiDocument, View view, IEnumerable<HighlightedElement> elements) {
        var scene = ClashHighlightScene.Create(view, elements);
        if(scene is null) {
            Clear(uiDocument);
            return;
        }

        EnsureRegistered();
        if(_server is null) {
            return;
        }
        _server.SetScene(scene);
        uiDocument.RefreshActiveView();
    }

    /// <summary>
    /// Убирает подсветку, сервер остаётся зарегистрированным.
    /// </summary>
    public static void Clear(UIDocument uiDocument) {
        if(_server is null) {
            return;
        }
        _server.SetScene(null);
        uiDocument?.RefreshActiveView();
    }

    /// <summary>
    /// Убирает подсветку и снимает сервер со службы.
    /// </summary>
    public static void Unregister(UIDocument uiDocument) {
        if(_server is null) {
            return;
        }

        _server.SetScene(null);
        if(GetService() is { } service && service.IsRegisteredServerId(_server.GetServerId())) {
            service.RemoveServer(_server.GetServerId());
        }
        _server = null;
        uiDocument?.RefreshActiveView();
    }

    private static void EnsureRegistered() {
        if(_server is not null) {
            return;
        }

        var service = GetService();
        if(service is null) {
            return;
        }

        var server = new ClashHighlightServer();
        service.AddServer(server);
        var active = service.GetActiveServerIds();
        active.Add(server.GetServerId());
        service.SetActiveServers(active);
        _server = server;
    }

    private static MultiServerService GetService() {
        return ExternalServiceRegistry.GetService(ExternalServices.BuiltInExternalServices.DirectContext3DService)
            as MultiServerService;
    }
}
