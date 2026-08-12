using System;
using System.Collections.Generic;

namespace Juru.RevitPlugins.ClashNavigator.Services.ClashStatuses;

/// <summary>
/// Результат отправки статусов коллизий в базу.
/// </summary>
internal class ClashStatusUpdateResult {
    private ClashStatusUpdateResult() { }

    /// <summary>
    /// Количество коллизий, статус которых записан в базу.
    /// </summary>
    public int Updated { get; private set; }

    /// <summary>
    /// Количество коллизий, статус которых отправлять некуда:
    /// они не загружены с сервера или их статус в базе не хранится.
    /// </summary>
    public int Skipped { get; private set; }

    /// <summary>
    /// Автор изменения, отправленный на сервер.
    /// </summary>
    public string? ChangedBy { get; private set; }

    /// <summary>
    /// Момент изменения, отправленный на сервер.
    /// </summary>
    public DateTime ChangedAt { get; private set; }

    /// <summary>
    /// Текст ошибки. Null, если сервер принял все статусы.
    /// </summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// Id коллизий, статус которых сервер не принял. Пусто, если запрос не дошёл целиком:
    /// тогда неотправленными считаются все коллизии.
    /// </summary>
    public IReadOnlyCollection<long> FailedClashIds { get; private set; } = [];

    /// <summary>
    /// Запрос дошёл до сервера, поэтому принятые статусы можно считать записанными,
    /// даже если часть коллизий попала в <see cref="FailedClashIds"/>.
    /// </summary>
    public bool RequestDelivered { get; private set; }

    public bool HasError => ErrorMessage is not null;

    public static ClashStatusUpdateResult Success(int updated, int skipped, string changedBy, DateTime changedAt) {
        return new ClashStatusUpdateResult {
            Updated = updated,
            Skipped = skipped,
            ChangedBy = changedBy,
            ChangedAt = changedAt,
            RequestDelivered = true
        };
    }

    /// <summary>
    /// Сервер ответил, но часть статусов не записал.
    /// </summary>
    public static ClashStatusUpdateResult PartialSuccess(
        int updated,
        int skipped,
        string changedBy,
        DateTime changedAt,
        IReadOnlyCollection<long> failedClashIds,
        string message) {

        var result = Success(updated, skipped, changedBy, changedAt);
        result.FailedClashIds = failedClashIds;
        result.ErrorMessage = message;
        return result;
    }

    /// <summary>
    /// Запрос до сервера не дошёл: неотправленными остаются все коллизии.
    /// </summary>
    public static ClashStatusUpdateResult Error(string message, int skipped = 0) {
        return new ClashStatusUpdateResult { ErrorMessage = message, Skipped = skipped };
    }
}
