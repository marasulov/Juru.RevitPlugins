using System;
using System.Text.Json;

namespace Juru.RevitPlugins.Schedules.Services.Translation;

/// <summary>
///     Приводит сбой обращения к службе перевода к <see cref="TranslationException" /> - единственному
///     исключению, которое службы перевода выпускают наружу.
/// </summary>
/// <remarks>
///     Недоступная служба - ожидаемый исход, о котором окно сообщает строкой состояния. Чужое
///     исключение из асинхронной команды сообщением не станет: оно уходит в планировщик задач и
///     закрывает окно, поэтому каждая служба переводит свои сбои сюда.
/// </remarks>
public static class TranslationFailure
{
    /// <summary>
    ///     Причина сбоя запроса вместе с внутренней: у <c>HttpRequestException</c> собственное
    ///     сообщение обычно общее («An error occurred while sending the request»), а имя узла,
    ///     отказ в соединении или сорванный TLS названы во вложенном исключении.
    /// </summary>
    public static string Describe(Exception exception)
    {
        var deepest = exception;

        while (deepest.InnerException is { } inner)
        {
            deepest = inner;
        }

        return ReferenceEquals(deepest, exception)
            ? exception.Message
            : $"{exception.Message} {deepest.Message}";
    }

    /// <summary>
    ///     Разбирает ответ службы. Вместо него приходит и страница прокси, и сообщение шлюза: разбор
    ///     такого текста падает, и это тоже про недоступность службы, а не про ошибку в коде.
    /// </summary>
    public static JsonDocument Parse(string body, string serviceName)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException exception)
        {
            throw new TranslationException($"{serviceName} вернул не ответ службы: {exception.Message}");
        }
    }
}
