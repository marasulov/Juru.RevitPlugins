using System;

namespace Juru.RevitPlugins.ClashNavigator.Models.RevitClashReport.ServerClashes.Dto;

/// <summary>
/// Тело запроса на изменение статусов коллизий из Ревита.
/// </summary>
internal class ClashStatusUpdateDto {
    /// <summary>
    /// Учётная запись Windows, под которой работает пользователь, в формате DOMAIN\user.
    /// </summary>
    public string ChangedBy { get; set; }

    /// <summary>
    /// Имя пользователя без домена - для отображения в интерфейсах.
    /// </summary>
    public string ChangedByDisplayName { get; set; }

    /// <summary>
    /// Момент изменения на машине пользователя, UTC.
    /// </summary>
    public DateTime ChangedAt { get; set; }

    /// <summary>
    /// Источник изменения, чтобы в базе было видно, откуда пришёл статус.
    /// </summary>
    public string Source { get; set; }

    /// <summary>
    /// Изменённые коллизии.
    /// </summary>
    public ClashStatusUpdateItemDto[] Items { get; set; } = [];
}

internal class ClashStatusUpdateItemDto {
    /// <summary>
    /// Id коллизии в базе сервера (<see cref="ClashViewDto.Id"/>).
    /// </summary>
    public long ClashId { get; set; }

    /// <summary>
    /// Новый статус коллизии: new, active, approved или resolved.
    /// </summary>
    public string Status { get; set; }
}

/// <summary>
/// Ответ сервера на запрос изменения статусов.
/// </summary>
internal class ClashStatusUpdateResultDto {
    /// <summary>
    /// Количество коллизий, статус которых записан в базу.
    /// </summary>
    public int Updated { get; set; }

    /// <summary>
    /// Коллизии, статус которых записать не удалось.
    /// </summary>
    public ClashStatusUpdateErrorDto[] Failed { get; set; } = [];
}

internal class ClashStatusUpdateErrorDto {
    public long ClashId { get; set; }
    public string Message { get; set; }
}
