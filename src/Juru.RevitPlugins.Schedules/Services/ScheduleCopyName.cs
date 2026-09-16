using System;

namespace Juru.RevitPlugins.Schedules.Services;

/// <summary>
///     Имя копии спецификации: то же имя, но с кодом языка перевода.
/// </summary>
/// <remarks>
///     В комплектах язык уже стоит в имени - «34 RU_Список сокращений», «20 RU_Шапка спецификации», - и
///     копия занимает то же место: «34 EN_Список сокращений». Имени без такой пометки код дописывается
///     спереди. Так копии сортируются рядом с оригиналами, а не собираются в конце списка под общим
///     суффиксом.
/// </remarks>
public static class ScheduleCopyName
{
    private const int CodeLength = 2;

    /// <param name="language">
    ///     Язык перевода из настроек: «en», «ru». Пустой не бывает, но если настройку затёрли, имя
    ///     остаётся прежним - пусть копия зовётся как оригинал и получит номер, чем окажется без имени.
    /// </param>
    public static string For(string original, string language)
    {
        if (original is null)
        {
            throw new ArgumentNullException(nameof(original));
        }

        var code = (language ?? string.Empty).Trim().ToUpperInvariant();

        if (code.Length is 0)
        {
            return original;
        }

        var existing = FindCode(original);

        return existing < 0
            ? $"{code}_{original}"
            : original[..existing] + code + original[(existing + CodeLength)..];
    }

    /// <summary>
    ///     Название таблицы из имени спецификации: без служебной части с номером комплекта и кодом языка.
    /// </summary>
    /// <remarks>
    ///     Именно оно попадает в строку названия копии, когда в оригинале эта строка не переопределена:
    ///     Ревит рисует там имя вида, а «36 RU_» - это про место в комплекте, а не про таблицу, и
    ///     переводить это незачем. Пометки нет - берётся имя целиком.
    /// </remarks>
    public static string TitleOf(string original)
    {
        if (original is null)
        {
            throw new ArgumentNullException(nameof(original));
        }

        var code = FindCode(original);

        return code < 0 ? original : original[(code + CodeLength + 1)..].Trim();
    }

    /// <summary>
    ///     Где в имени стоит код языка: две буквы подряд перед подчёркиванием, не продолжающие слово.
    ///     <c>-1</c>, если такой пометки нет.
    /// </summary>
    private static int FindCode(string name)
    {
        for (var index = 0; index + CodeLength < name.Length; index++)
        {
            if (name[index + CodeLength] is not '_')
            {
                continue;
            }

            if (index > 0 && (char.IsLetterOrDigit(name[index - 1]) || name[index - 1] is '_'))
            {
                continue;
            }

            if (IsCode(name.Substring(index, CodeLength)))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsCode(string part)
    {
        foreach (var symbol in part)
        {
            if (!char.IsUpper(symbol))
            {
                return false;
            }
        }

        return true;
    }
}
