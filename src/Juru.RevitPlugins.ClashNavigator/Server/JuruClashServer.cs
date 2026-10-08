using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using dosymep.Bim4Everyone;
using dosymep.SimpleServices;

using ClashNavigator.Models;
using ClashNavigator.Models.Clashes;
using ClashNavigator.Models.RevitClashReport;
using ClashNavigator.Server.Dto;
using ClashNavigator.Services.ClashServer;

using Juru.RevitPlugins.Core.FileNaming;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ClashNavigator.Server;

/// <summary>
/// Сервер коллизий организации: отчёт открытого файла находится по имени файла, разобранному
/// по стандарту наименования, статусы уходят через REST API сервера.
/// </summary>
internal class JuruClashServer : IClashServer {
    private const string _source = "Revit ClashNavigator";
    private const string _settingsFileName = "ClashServerSettings.json";

    private readonly string _settingsPath;

    public JuruClashServer() {
        _settingsPath = Path.Combine(GetSettingsDirectory(), _settingsFileName);
        LoadSettings();
    }

    public string Url => ClashServerOptions.ResolveUrl(UserUrl);

    public string UserUrl { get; set; } = string.Empty;

    public bool AutoLoad { get; set; }

    public string CurrentUserName => WindowsUser.FullName;

    public void SaveSettings() {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            var settings = new ServerSettings { Url = UserUrl, AutoLoad = AutoLoad };
            File.WriteAllText(_settingsPath, JsonConvert.SerializeObject(settings, Formatting.Indented));
        } catch(Exception exception) when(exception is IOException or UnauthorizedAccessException) {
            // Настройка удобства: не записалась - при следующем открытии будет значение по умолчанию.
        }
    }

    public ReportModel LoadReport(RevitRepository revitRepository, ILocalizationService localizationService) {
        string fileName = revitRepository.GetDocumentName();
        if(!RevitFileNameInfo.TryParse(fileName, out var fileInfo)) {
            throw new ClashServerException(Text(
                $"Имя файла \"{fileName}\" не соответствует стандарту наименования (Проект-Объект-Дисциплина-vXX). "
                + "Дисциплина - одно из: ARC, STR, WSS, ELE, HVC, FAD.",
                $"The file name \"{fileName}\" does not follow the naming standard (Project-Object-Discipline-vXX). "
                + "The discipline is one of: ARC, STR, WSS, ELE, HVC, FAD."));
        }

        string url = Url;
        if(string.IsNullOrWhiteSpace(url)) {
            throw new ClashServerException(Text(
                "Не задан адрес сервера коллизий: ни в настройках плагина, ни в файле ClashServer.json.",
                "No clash server address is set, neither in the plugin settings nor in ClashServer.json."));
        }

        var client = new ClashServerClient(url);
        return ServerReportLoader.GetReportAsync(client, fileInfo, revitRepository)
            .ConfigureAwait(false).GetAwaiter().GetResult();
    }

    public ClashStatus ToClashStatus(string serverStatus) {
        return ServerClashStatus.ToClashStatus(serverStatus);
    }

    public string ToServerStatus(ClashStatus status) {
        return ServerClashStatus.ToServerStatus(status);
    }

    public ClashStatusUpdateResult UpdateStatuses(ICollection<ClashModel> clashes) {
        if(clashes is null) {
            throw new ArgumentNullException(nameof(clashes));
        }

        var items = clashes
            .Where(CanUpdate)
            .Select(clash => new ClashStatusUpdateItemDto {
                // CanUpdate уже проверил, что Id коллизии на сервере известен.
                ClashId = clash.ServerId!.Value,
                Status = ServerClashStatus.ToServerStatus(clash.ClashStatus)
            })
            .ToArray();
        int skipped = clashes.Count - items.Length;
        if(items.Length == 0) {
            return ClashStatusUpdateResult.Success(0, skipped, WindowsUser.Name, DateTime.UtcNow);
        }

        string url = Url;
        if(string.IsNullOrWhiteSpace(url)) {
            return ClashStatusUpdateResult.Error(Text(
                "Не задан адрес сервера коллизий: ни в настройках плагина, ни в файле ClashServer.json.",
                "No clash server address is set, neither in the plugin settings nor in ClashServer.json."), skipped);
        }

        var changedAt = DateTime.UtcNow;
        var request = new ClashStatusUpdateDto {
            ChangedBy = WindowsUser.Name,
            // В базу отправляем чистое имя сотрудника, без учётной записи в скобках.
            ChangedByDisplayName = WindowsUser.FullName,
            ChangedAt = changedAt,
            Source = _source,
            Items = items
        };

        try {
            var client = new ClashServerClient(url);
            // Окно навигатора работает в потоке UI, поэтому ждём ответ здесь же,
            // как и загрузка отчёта с сервера.
            var result = client.UpdateClashStatusesAsync(request)
                .ConfigureAwait(false).GetAwaiter().GetResult();

            var failed = result?.Failed ?? [];
            if(failed.Length > 0) {
                return ClashStatusUpdateResult.PartialSuccess(
                    items.Length - failed.Length,
                    skipped,
                    request.ChangedBy,
                    changedAt,
                    [.. failed.Select(f => f.ClashId)],
                    Text($"сервер не записал статус {failed.Length} коллизий: {FormatErrors(failed)}",
                        $"the server did not store the status of {failed.Length} clashes: {FormatErrors(failed)}"));
            }

            // Если сервер не вернул количество, считаем записанным всё, что отправили:
            // отсутствие ошибок для нас и есть успех.
            int updated = result is null || result.Updated == 0 ? items.Length : result.Updated;
            return ClashStatusUpdateResult.Success(updated, skipped, request.ChangedBy, changedAt);
        } catch(Exception exception) {
            return ClashStatusUpdateResult.Error(exception.GetBaseException().Message, skipped);
        }
    }

    private static bool CanUpdate(ClashModel clash) {
        return clash?.ServerId is not null && ServerClashStatus.ToServerStatus(clash.ClashStatus) is not null;
    }

    private static string FormatErrors(IEnumerable<ClashStatusUpdateErrorDto> errors) {
        return string.Join("; ", errors.Take(3).Select(e => $"#{e.ClashId} - {e.Message}"));
    }

    /// <summary>
    /// Сообщения сервера - только у этой редакции, поэтому они здесь, а не в общих словарях.
    /// </summary>
    private static string Text(string russian, string english) {
        return Services.PluginLocalization.Culture.TwoLetterISOLanguageName == "ru" ? russian : english;
    }

    /// <summary>
    /// Папка настроек плагина - та же, где лежат его общие настройки (SettingsConfig.json).
    /// </summary>
    private static string GetSettingsDirectory() {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "dosymep",
            ModuleEnvironment.RevitVersion,
            PluginEdition.ProfileName);
    }

    private void LoadSettings() {
        try {
            if(File.Exists(_settingsPath)) {
                var settings = JsonConvert.DeserializeObject<ServerSettings>(File.ReadAllText(_settingsPath));
                UserUrl = settings?.Url ?? string.Empty;
                AutoLoad = settings?.AutoLoad ?? false;
                return;
            }

            // До общего ядра адрес и автозагрузка лежали в SettingsConfig.json: переносим их,
            // чтобы обновление плагина не сбросило настройку пользователя.
            string legacyPath = Path.Combine(Path.GetDirectoryName(_settingsPath)!, "SettingsConfig.json");
            if(File.Exists(legacyPath)) {
                var legacy = JObject.Parse(File.ReadAllText(legacyPath));
                UserUrl = legacy.Value<string>("ClashServerUrl") ?? string.Empty;
                AutoLoad = legacy.Value<bool?>("AutoLoadFromServer") ?? false;
            }
        } catch(Exception exception) when(exception is IOException or UnauthorizedAccessException or JsonException) {
            UserUrl = string.Empty;
            AutoLoad = false;
        }
    }

    private sealed class ServerSettings {
        public string Url { get; set; }

        public bool AutoLoad { get; set; }
    }
}
