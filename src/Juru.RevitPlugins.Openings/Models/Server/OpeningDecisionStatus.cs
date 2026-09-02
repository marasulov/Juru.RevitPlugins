using System;

namespace Juru.RevitPlugins.Openings.Models.Server;

/// <summary>
/// Значения статуса согласования из контракта сервера (<c>docs/OpeningsApi.md</c>) и сравнение
/// с ними.
/// </summary>
/// <remarks>
/// Сравнение регистронезависимое: контракт задаёт <c>approved</c> строчными, но статус приходит
/// строкой, а не перечислением, и сериализация на стороне сервера может отдать <c>Approved</c>.
/// Строгое сравнение в этом случае выключало бы кнопку "Вставить" на согласованном задании, не
/// объясняя причины, - разбираться пришлось бы через отладчик.
/// </remarks>
public static class OpeningDecisionStatus {
    public const string Sent = "sent";
    public const string PartiallyApproved = "partiallyApproved";
    public const string Approved = "approved";
    public const string Rejected = "rejected";

    /// <summary>
    /// Статус равен ожидаемому значению контракта.
    /// </summary>
    public static bool Is(string status, string expected) {
        return string.Equals(status, expected, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Задание согласовано - обеими дисциплинами, если это общий статус задания, либо этой
    /// дисциплиной, если это решение одной из них.
    /// </summary>
    public static bool IsApproved(string status) {
        return Is(status, Approved);
    }

    public static bool IsRejected(string status) {
        return Is(status, Rejected);
    }

    public static bool IsSent(string status) {
        return Is(status, Sent);
    }
}
