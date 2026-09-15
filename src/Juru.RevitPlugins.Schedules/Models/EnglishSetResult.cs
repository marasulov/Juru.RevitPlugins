using System;
using System.Collections.Generic;

namespace Juru.RevitPlugins.Schedules.Models;

/// <summary>
///     Итог сборки английского комплекта по отмеченному в окне.
/// </summary>
[Serializable]
public sealed record EnglishSetResult
{
    /// <summary>Спецификаций отрисовано.</summary>
    public int DrawnCount { get; init; }

    /// <summary>Спецификаций, на которых отрисовка сорвалась.</summary>
    public int FailedCount { get; init; }

    /// <summary>Строк глоссария, переведённых машиной в этом прогоне.</summary>
    public int TranslatedNowCount { get; init; }

    /// <summary>Строк глоссария, у которых так и нет перевода.</summary>
    public int UntranslatedCount { get; init; }

    /// <summary>Отмечено в категориях, но перевода так и нет: записывать было нечего.</summary>
    public int WithoutTranslationCount { get; init; }

    /// <summary>Что стало с переводами категорий модели.</summary>
    public IReadOnlyList<ElementTextApplyResult> Categories { get; init; } = [];

    public string? ReportPath { get; init; }

    /// <summary>Путь переведённой таблицы ключевых примечаний, если она была записана.</summary>
    public string? KeynotePath { get; init; }

    public string? TranslatorNote { get; init; }

    public IReadOnlyList<string> Failures { get; init; } = [];
}

/// <summary>
///     Что записать в одну категорию модели. Ключ устойчив, имя переведено: сборка отбирает
///     категории по ключу, а в отчёт пишет имя.
/// </summary>
public sealed record CategoryWrite(string Key, string Name, IReadOnlyList<CategoryValue> Values);

/// <summary>
///     Один элемент и его перевод так, как он стоит в окне.
/// </summary>
/// <remarks>
///     Именно перевод, а не готовое значение: пустой означает «в окне не переведено», и тогда перевод
///     берётся из глоссария, куда его только что дописал машинный проход. Складывать исходник
///     с переводом должна сборка, у которой на руках и глоссарий, и выбранный режим.
/// </remarks>
public sealed record CategoryValue(ElementText Text, string Translation);
