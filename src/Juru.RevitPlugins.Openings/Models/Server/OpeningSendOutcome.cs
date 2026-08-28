namespace Juru.RevitPlugins.Openings.Models.Server;

/// <summary>
/// Итог отправки заданий на сервер - см. <see cref="OpeningTasksSender"/>.
/// </summary>
public class OpeningSendOutcome {
    /// <summary>
    /// Сколько заданий ушло. Ноль означает, что отправки не было.
    /// </summary>
    public int Sent { get; set; }

    /// <summary>
    /// Что показать пользователю - готовый текст, одинаковый и для диалога с ленты,
    /// и для шапки навигатора.
    /// </summary>
    public string Report { get; set; } = string.Empty;

    /// <summary>
    /// Отправка не состоялась: отправлять было нечего или условия не позволили.
    /// </summary>
    public bool IsRefused => Sent == 0;

    public static OpeningSendOutcome Refused(string reason) {
        return new OpeningSendOutcome { Sent = 0, Report = reason };
    }
}
