using System;
using System.IO;

namespace Juru.RevitPlugins.Core.Profiles;

/// <summary>
/// Каталоги, в которых плагины хранят настройки.
/// </summary>
public static class ProfilePaths {
    private const string _sharedProfilePath =
        @"W:\Проектный институт\Отд.стандарт.BIM и RD\BIM-Ресурсы\5-Надстройки\Bim4Everyone\A101";

    /// <summary>
    /// Общий каталог настроек в сетевой папке института, либо <see cref="Local"/>, если папка недоступна.
    /// </summary>
    /// <remarks>
    /// Общие настройки раскатывает BIM-менеджер: плагин читает их, когда у пользователя нет своих.
    /// </remarks>
    public static string Shared {
        get {
            return Directory.Exists(_sharedProfilePath) ? _sharedProfilePath : Local;
        }
    }

    /// <summary>
    /// Каталог настроек пользователя. Плагин пишет только сюда.
    /// </summary>
    public static string Local { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "dosymep");

    /// <summary>
    /// Проверяет, доступна ли сетевая папка с общими настройками.
    /// </summary>
    public static bool SharedAvailable => Directory.Exists(_sharedProfilePath);
}
