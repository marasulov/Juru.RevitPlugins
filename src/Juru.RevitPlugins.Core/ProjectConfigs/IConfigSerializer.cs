using JetBrains.Annotations;

namespace Juru.RevitPlugins.Core.ProjectConfigs;

/// <summary>
/// Читает и записывает конфигурации плагина.
/// </summary>
[PublicAPI]
public interface IConfigSerializer {
    /// <summary>
    /// Восстанавливает объект из текста.
    /// </summary>
    /// <typeparam name="T">Тип восстанавливаемого объекта.</typeparam>
    /// <param name="text">Текст конфигурации.</param>
    /// <returns>Возвращает восстановленный объект.</returns>
    T Deserialize<T>(string text);

    /// <summary>
    /// Преобразует объект в текст.
    /// </summary>
    /// <typeparam name="T">Тип преобразуемого объекта.</typeparam>
    /// <param name="object">Преобразуемый объект.</param>
    /// <returns>Возвращает текст конфигурации.</returns>
    string Serialize<T>(T @object);
}
