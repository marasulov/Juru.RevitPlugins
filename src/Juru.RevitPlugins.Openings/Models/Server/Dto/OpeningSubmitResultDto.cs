using System.Collections.Generic;

namespace Juru.RevitPlugins.Openings.Models.Server.Dto;

/// <summary>
/// Ответ сервера на <c>POST api/openings</c>.
/// </summary>
public class OpeningSubmitResultDto {
    public int Accepted { get; set; }
    public int Updated { get; set; }
    public List<OpeningSubmitFailureDto> Failed { get; set; } = [];
}

public class OpeningSubmitFailureDto {
    public string TaskKey { get; set; }
    public string Message { get; set; }
}
