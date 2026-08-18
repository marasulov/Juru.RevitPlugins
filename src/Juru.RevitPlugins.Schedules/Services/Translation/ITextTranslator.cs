using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Juru.RevitPlugins.Schedules.Services.Translation;

/// <summary>
///     Служба перевода текста. Порядок и количество возвращённых строк совпадают с запрошенными.
/// </summary>
public interface ITextTranslator
{
    string Name { get; }

    Task<IReadOnlyList<string>> TranslateAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken);
}
