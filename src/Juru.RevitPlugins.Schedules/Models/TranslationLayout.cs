using System;
using System.Collections.Generic;

using Juru.RevitPlugins.Schedules.Localization;

namespace Juru.RevitPlugins.Schedules.Models;

/// <summary>
///     Что именно попадает в параметр элемента: только перевод или исходник и перевод рядом.
/// </summary>
/// <remarks>
///     Двуязычное значение нужно там, где на листе один и тот же параметр печатается и в русском,
///     и в английском комплекте: марка помещения тогда читается обеими сторонами без второго
///     параметра и без второй марки.
/// </remarks>
public sealed record TranslationLayout
{
    /// <summary>Разделители, которые не путаются с текстом названия.</summary>
    public static readonly string[] Separators = ["/", "-", ";", "|", "—", NewLine];

    /// <summary>Перевод с новой строки — единственный разделитель без символа рядом с текстом.</summary>
    public const string NewLine = "\n";

    /// <summary>То же самое, но с читаемым названием для выпадающего списка.</summary>
    public static IReadOnlyList<SeparatorOption> SeparatorOptions { get; } =
    [
        new SeparatorOption("/", "/"),
        new SeparatorOption("-", "-"),
        new SeparatorOption(";", ";"),
        new SeparatorOption("|", "|"),
        new SeparatorOption("—", "—"),
        new SeparatorOption(NewLine, Pick("Новая строка", "New line"))
    ];

    /// <summary>Оставлять исходник перед переводом.</summary>
    public bool KeepSource { get; init; }

    public string Separator { get; init; } = "/";

    /// <summary>
    ///     Собирает значение для записи. Пустой перевод остаётся пустым в любом случае: пустая клетка
    ///     «Перевод» означает откат, и двуязычный режим не должен превращать её в запись исходника.
    /// </summary>
    public string Compose(string sourceText, string translation)
    {
        var trimmed = translation?.Trim() ?? string.Empty;

        if (trimmed.Length is 0 || !KeepSource)
        {
            return trimmed;
        }

        var source = sourceText.Trim();

        return Separator is NewLine ? $"{source}{NewLine}{trimmed}" : $"{source} {Separator} {trimmed}";
    }

    /// <summary>
    ///     Достаёт перевод из уже записанного двуязычного значения. Нужно при повторном открытии окна:
    ///     иначе перевод «Кабинет / Office» ушёл бы в клетку целиком и следующая запись удвоила бы
    ///     исходник.
    /// </summary>
    public static string StripSource(string sourceText, string currentText)
    {
        var source = sourceText.Trim();

        foreach (var separator in Separators)
        {
            var prefix = separator is NewLine ? $"{source}{NewLine}" : $"{source} {separator} ";

            if (currentText.StartsWith(prefix, StringComparison.CurrentCulture))
            {
                return currentText.Substring(prefix.Length).Trim();
            }
        }

        return currentText;
    }

    private static string Pick(string russian, string english)
    {
        return Text.Language is PluginLanguage.Russian ? russian : english;
    }
}

/// <summary>Один разделитель в выпадающем списке: символ для записи и читаемое название.</summary>
public sealed record SeparatorOption(string Value, string Label);
