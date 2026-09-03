namespace Juru.RevitPlugins.Openings.Models.Server.Dto;

/// <summary>
/// Проект в справочнике сервера (<c>GET api/projects</c>).
/// </summary>
public class ServerProjectDto {
    public int Id { get; set; }
    public string Name { get; set; }
}

/// <summary>
/// Объект проекта в справочнике сервера (<c>GET api/projects/{id}/objects</c>).
/// </summary>
public class ServerObjectDto {
    public int Id { get; set; }
    public string Name { get; set; }
}
