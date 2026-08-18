namespace Juru.RevitPlugins.Schedules.Models;

/// <summary>
///     Перевод строки вместе с его происхождением.
/// </summary>
public sealed record GlossaryTranslation(string Text, TranslationSource Source);
