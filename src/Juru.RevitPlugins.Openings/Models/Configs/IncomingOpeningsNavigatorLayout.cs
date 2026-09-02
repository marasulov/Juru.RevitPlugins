using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

using Juru.RevitPlugins.Core;
using Juru.RevitPlugins.Core.Profiles;

namespace Juru.RevitPlugins.Openings.Models.Configs;

/// <summary>
/// Что пользователь настроил под себя в окне согласования: какие колонки скрыты и какие решения
/// показывать.
/// </summary>
/// <remarks>
/// Отдельный файл в профиле пользователя, а не поле в <see cref="OpeningsConfig"/>: тот раскатывает
/// BIM-менеджер на всех, а нужные колонки и фильтр - дело того, кто согласовывает. Общего
/// варианта у этих настроек нет, поэтому и чтения из сетевого профиля здесь нет.
/// </remarks>
public class IncomingOpeningsNavigatorLayout {
    private const string _pluginFolderName = "Openings";
    private const string _fileName = nameof(IncomingOpeningsNavigatorLayout) + ".json";

    private static readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    /// <summary>
    /// Заголовки скрытых колонок. Хранятся заголовками, а не номерами: номер съезжает, как только
    /// в гриде появляется новая колонка, и пользователь получал бы скрытой не ту, что убрал.
    /// </summary>
    public List<string> HiddenColumns { get; set; } = [];

    public bool ShowPending { get; set; } = true;

    public bool ShowApproved { get; set; } = true;

    public bool ShowRejected { get; set; } = true;

    /// <summary>
    /// Читает настройки окна. Если файла нет или он испорчен - настройки по умолчанию: показывать
    /// всё. Окно не должно открываться с ошибкой из-за своих же украшений.
    /// </summary>
    public static IncomingOpeningsNavigatorLayout Load() {
        try {
            string path = GetPath();
            return File.Exists(path)
                ? JsonSerializer.Deserialize<IncomingOpeningsNavigatorLayout>(File.ReadAllText(path), _options)
                    ?? new IncomingOpeningsNavigatorLayout()
                : new IncomingOpeningsNavigatorLayout();
        } catch(Exception exception) when(exception is IOException
            or UnauthorizedAccessException
            or JsonException) {
            return new IncomingOpeningsNavigatorLayout();
        }
    }

    /// <summary>
    /// Сохраняет настройки окна. Ошибку записи глотает: сорванное сохранение украшений не должно
    /// прерывать согласование.
    /// </summary>
    public void Save() {
        try {
            string path = GetPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonSerializer.Serialize(this, _options));
        } catch(Exception exception) when(exception is IOException or UnauthorizedAccessException) {
            // Настройки останутся прежними при следующем запуске - это всё, чем это грозит.
        }
    }

    private static string GetPath() {
        return Path.Combine(ProfilePaths.Local, RevitVersionInfo.Version, _pluginFolderName, _fileName);
    }
}
