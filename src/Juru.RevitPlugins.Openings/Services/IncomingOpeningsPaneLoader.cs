using Autodesk.Revit.UI;

using Juru.RevitPlugins.Core.FileNaming;
using Juru.RevitPlugins.Openings.Models.Configs;
using Juru.RevitPlugins.Openings.Models.Server;
using Juru.RevitPlugins.Openings.ViewModels.Navigator;
using Juru.RevitPlugins.Openings.Views.Navigator;

namespace Juru.RevitPlugins.Openings.Services;

/// <summary>
/// Собирает содержимое панели согласования по активному файлу: читает задания с сервера, ставит их
/// в модели и подставляет панели модель представления.
/// </summary>
/// <remarks>
/// Отдельно от команды ленты, потому что зовут это из двух мест: сама команда и панель, когда Revit
/// открыл её при запуске (положение панелей он помнит) - тогда никто кнопку не нажимал, а панель
/// уже на экране и должна показать задания.
/// </remarks>
public static class IncomingOpeningsPaneLoader {
    /// <summary>
    /// Собирает панель. Вызывается только в контексте Revit API.
    /// </summary>
    /// <returns>
    /// <c>null</c>, если панель собрана, иначе причина, по которой собрать её нельзя, - команда
    /// ленты показывает её пользователем, автозагрузка молчит: панель просто остаётся пустой.
    /// </returns>
    public static string Load(UIApplication uiApplication) {
        var doc = uiApplication?.ActiveUIDocument?.Document;
        if(doc is null) {
            return "Нет открытого файла.";
        }

        var config = OpeningsConfig.GetOpeningsConfig();

        if(!RevitFileNameInfo.TryParse(doc.PathName, out var fileInfo)) {
            return "Имя файла не соответствует стандарту наименования "
                + "(Проект-Объект-Дисциплина-vNN): без него нельзя определить проект и объект "
                + "для сервера.";
        }

        if(!fileInfo.Discipline.ReceivesOpeningTasks()) {
            return "Дисциплина файла - " + fileInfo.Discipline
                + ". Согласование работает в файлах ARC и STR.";
        }

        string serverUrl = OpeningsServerOptions.ResolveUrl(config);
        if(string.IsNullOrWhiteSpace(serverUrl)) {
            return "Адрес сервера не задан ни в настройках плагина, ни в ClashServer.json рядом со сборкой.";
        }

        var client = new OpeningsServerClient(serverUrl);
        var navigator = new IncomingOpeningsNavigatorViewModel(
            config, new RevitActionRunner(), fileInfo.Discipline, fileInfo, client);
        // Загрузка идёт прямо здесь: мы уже в контексте API, внешнее событие для неё не нужно.
        // Дальше "Обновить" в панели пойдёт через событие самого навигатора.
        navigator.Reload(uiApplication);

        IncomingOpeningsPane.Attach(new IncomingOpeningsPaneViewModel(navigator));
        return null;
    }
}
