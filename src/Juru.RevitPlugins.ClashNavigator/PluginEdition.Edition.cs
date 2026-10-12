using System;

using ClashNavigator.Server;
using ClashNavigator.Services.ClashServer;

using Juru.RevitPlugins.Core.Profiles;

namespace ClashNavigator;

/// <summary>
/// Редакция организации: общий профиль в сетевой папке и сервер коллизий.
/// </summary>
internal static partial class PluginEdition {
    public const string AssemblyName = "Juru.RevitPlugins.ClashNavigator";

    public const string LocalizationResourceName =
        "/Juru.RevitPlugins.ClashNavigator;component/assets/Localization/Language.xaml";

    /// <summary>
    /// Прежнее имя папки: в ней уже лежат проверки, наборы и отчёты пользователей.
    /// </summary>
    public const string ProfileName = "ClashNavigator";

    public const string VendorId = "Juru";

    /// <summary>
    /// Имена, под которыми вид и фильтры уже созданы в моделях организации.
    /// </summary>
    public const string DefaultClashViewName = "BIM_Проверка на коллизии";

    public const string DefaultViewFilterPrefix = "BIM_коллизии_";

    /// <summary>
    /// Параметр группы видов по стандарту Bim4Everyone, принятому в организации.
    /// </summary>
    public const string ViewGroupParameterName = "_Группа Видов";

    public static string LocalProfilePath => ProfilePaths.Local;

    /// <summary>
    /// Общие проверки и наборы раскатывает BIM-менеджер в сетевую папку института;
    /// без доступа к ней - профиль пользователя.
    /// </summary>
    public static string ProfilePath => ProfilePaths.Shared;

    public static IClashServer CreateClashServer() => new JuruClashServer();

    public static readonly Guid NavigatorPaneId = new("E216EA17-A6AF-4303-B718-4462B6CDB154");
}
