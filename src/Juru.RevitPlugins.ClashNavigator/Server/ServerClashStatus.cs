using ClashNavigator.Models.Clashes;

namespace ClashNavigator.Server;

/// <summary>
/// Сопоставление статусов коллизии плагина со статусами в базе сервера.
/// </summary>
internal static class ServerClashStatus {
    public const string New = "new";
    public const string Active = "active";
    public const string Approved = "approved";
    public const string Resolved = "resolved";

    public static ClashStatus ToClashStatus(string status) {
        return status?.ToLowerInvariant() switch {
            New or Active => ClashStatus.Active,
            Approved => ClashStatus.Analized,
            Resolved => ClashStatus.Solved,
            _ => ClashStatus.Active
        };
    }

    /// <summary>
    /// Возвращает статус для отправки на сервер или null, если статус на сервере не хранится.
    /// </summary>
    /// <param name="status">Статус коллизии в плагине</param>
    public static string ToServerStatus(ClashStatus status) {
        return status switch {
            ClashStatus.Active => Active,
            ClashStatus.Analized => Approved,
            ClashStatus.Solved => Resolved,
            // "Нет пересечения" - результат локального пересчёта геометрии в Ревите,
            // такого статуса в базе сервера нет.
            _ => null
        };
    }
}
