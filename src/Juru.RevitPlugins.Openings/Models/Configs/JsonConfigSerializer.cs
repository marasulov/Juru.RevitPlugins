using System.Text.Json;
using System.Text.Json.Serialization;

using Juru.RevitPlugins.Core.ProjectConfigs;

namespace Juru.RevitPlugins.Openings.Models.Configs;

/// <summary>
/// Читает и записывает настройки плагина отверстий как JSON.
/// </summary>
/// <remarks>
/// Перечисления пишутся именами, а не числами: файл настроек правит руками BIM-менеджер,
/// а вставка нового значения в середину перечисления не должна менять смысл уже сохранённых файлов.
/// </remarks>
public class JsonConfigSerializer : IConfigSerializer {
    private static readonly JsonSerializerOptions _options = new() {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public T Deserialize<T>(string text) {
        return JsonSerializer.Deserialize<T>(text, _options);
    }

    public string Serialize<T>(T @object) {
        return JsonSerializer.Serialize(@object, _options);
    }
}
