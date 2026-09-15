using System.Collections.Generic;

using Juru.RevitPlugins.Schedules.Localization;

namespace Juru.RevitPlugins.Schedules.Services.Translation;

/// <summary>Один язык в выпадающем списке: код для службы перевода и читаемое название.</summary>
public sealed record LanguageOption(string Code, string Name);

/// <summary>
///     Языки, доступные в полях «От» и «До». Список — коды, которые понимают и DeepL, и Google:
///     свободный ввод убран, потому что опечатка в коде превращалась либо в непонятный ответ службы
///     перевода, либо — для целевого языка ключевых примечаний — в исключение при записи файла, чьё
///     имя строится из этого кода.
/// </summary>
public static class LanguageOptions
{
    public static IReadOnlyList<LanguageOption> All { get; } =
    [
        new LanguageOption("ru", Pick("Русский", "Russian")),
        new LanguageOption("en", Pick("Английский", "English")),
        new LanguageOption("de", Pick("Немецкий", "German")),
        new LanguageOption("fr", Pick("Французский", "French")),
        new LanguageOption("es", Pick("Испанский", "Spanish")),
        new LanguageOption("it", Pick("Итальянский", "Italian")),
        new LanguageOption("pt", Pick("Португальский", "Portuguese")),
        new LanguageOption("nl", Pick("Нидерландский", "Dutch")),
        new LanguageOption("pl", Pick("Польский", "Polish")),
        new LanguageOption("cs", Pick("Чешский", "Czech")),
        new LanguageOption("sv", Pick("Шведский", "Swedish")),
        new LanguageOption("fi", Pick("Финский", "Finnish")),
        new LanguageOption("tr", Pick("Турецкий", "Turkish")),
        new LanguageOption("uk", Pick("Украинский", "Ukrainian")),
        new LanguageOption("ar", Pick("Арабский", "Arabic")),
        new LanguageOption("zh", Pick("Китайский", "Chinese")),
        new LanguageOption("ja", Pick("Японский", "Japanese")),
        new LanguageOption("ko", Pick("Корейский", "Korean"))
    ];

    private static string Pick(string russian, string english)
    {
        return Text.Language is PluginLanguage.Russian ? russian : english;
    }
}
