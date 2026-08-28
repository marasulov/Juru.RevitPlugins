using System.Collections.Generic;

namespace Juru.RevitPlugins.Openings.Models.Server.Dto;

/// <summary>
/// Ответ сервера на <c>POST api/openings/status</c>.
/// </summary>
public class OpeningStatusResultDto {
    public int Updated { get; set; }
    public List<OpeningStatusFailureDto> Failed { get; set; } = [];
}

public class OpeningStatusFailureDto {
    public string TaskKey { get; set; }
    public string Message { get; set; }
}
