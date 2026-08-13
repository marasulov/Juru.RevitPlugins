using System;
using System.Collections.Generic;
using System.Linq;

using Juru.RevitPlugins.ClashNavigator.Models;
using Juru.RevitPlugins.ClashNavigator.Models.Clashes;
using Juru.RevitPlugins.ClashNavigator.Models.RevitClashReport.ServerClashes;
using Juru.RevitPlugins.ClashNavigator.Models.RevitClashReport.ServerClashes.Dto;

namespace Juru.RevitPlugins.ClashNavigator.Services.ClashStatuses;

/// <summary>
/// Отправляет изменённые статусы коллизий в базу через REST API сервера коллизий.
/// </summary>
internal class ServerClashStatusService : IClashStatusService {
    private const string _source = "Revit ClashNavigator";

    private readonly SettingsConfig _settingsConfig;

    public ServerClashStatusService(SettingsConfig settingsConfig) {
        _settingsConfig = settingsConfig ?? throw new ArgumentNullException(nameof(settingsConfig));
    }

    public bool CanUpdate(ClashModel clash) {
        return clash?.ServerId is not null && ServerClashStatus.ToServerStatus(clash.ClashStatus) is not null;
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

        string serverUrl = ClashServerOptions.ResolveUrl(_settingsConfig);
        if(string.IsNullOrWhiteSpace(serverUrl)) {
            return ClashStatusUpdateResult.Error(
                "Не задан адрес сервера коллизий: ни в настройках плагина, ни в файле ClashServer.json.", skipped);
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
            var client = new ClashServerClient(serverUrl);
            // Окно навигатора модальное и работает в потоке UI, поэтому ждём ответ здесь же,
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
                    $"сервер не записал статус {failed.Length} коллизий: {FormatErrors(failed)}");
            }

            // Если сервер не вернул количество, считаем записанным всё, что отправили:
            // отсутствие ошибок для нас и есть успех.
            int updated = result is null || result.Updated == 0 ? items.Length : result.Updated;
            return ClashStatusUpdateResult.Success(updated, skipped, request.ChangedBy, changedAt);
        } catch(Exception exception) {
            return ClashStatusUpdateResult.Error(exception.GetBaseException().Message, skipped);
        }
    }

    private static string FormatErrors(IEnumerable<ClashStatusUpdateErrorDto> errors) {
        return string.Join("; ", errors.Take(3).Select(e => $"#{e.ClashId} - {e.Message}"));
    }
}
