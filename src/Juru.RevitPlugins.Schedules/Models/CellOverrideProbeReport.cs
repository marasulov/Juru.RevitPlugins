using System;
using System.Collections.Generic;

namespace Juru.RevitPlugins.Schedules.Models;

/// <summary>
///     Проверка того, можно ли построить английскую спецификацию копированием текста: пишется ли перевод
///     поверх ячейки как оформление и читается ли отрендеренный текст надёжно.
/// </summary>
[Serializable]
public sealed record CellOverrideProbeReport
{
    public required string ModelName { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required string ScheduleName { get; init; }

    public required bool DataWasOutOfDate { get; init; }
    public required int BodyRowsBeforeRefresh { get; init; }
    public required int BodyRowsAfterRefresh { get; init; }
    public required int BodyColumns { get; init; }

    /// <summary>Ошибка записи заголовка колонки; заголовки задаются отдельно от ячеек.</summary>
    public string? ColumnHeadingError { get; init; }

    /// <summary>Ошибка записи текста в секцию шапки. Пусто — секция принимает произвольный текст.</summary>
    public string? HeaderCellError { get; init; }

    /// <summary>Разрешает ли Revit добавить строку в тело: путь к статической таблице из вставленных строк.</summary>
    public required bool BodyAllowsInsertRow { get; init; }

    /// <summary>Ошибка вставки строки и записи в неё. Пусто — вставленная строка принимает текст.</summary>
    public string? InsertedRowError { get; init; }

    public required IReadOnlyList<CellOverrideProbeAttempt> Attempts { get; init; }

    /// <summary>Текст первых ячеек копии: показывает, читаются ли данные вообще.</summary>
    public required IReadOnlyList<string> SampleCellTexts { get; init; }

    public string? Failure { get; init; }
}
