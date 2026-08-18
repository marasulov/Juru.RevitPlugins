using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Schedules.Models;

using RevitException = Autodesk.Revit.Exceptions.ApplicationException;

namespace Juru.RevitPlugins.Schedules.Services;

/// <summary>
///     Выясняет, ложится ли перевод в ячейку копии спецификации как оформление или уходит в параметр
///     элемента. Всё делается в транзакции, которая откатывается: в модели не остаётся ни копии, ни правок.
/// </summary>
public sealed class CellOverrideProbe(Document document)
{
    private const string ProbeText = "PROBE_EN";
    private const int MaxAttempts = 3;
    private const int MaxSampleCells = 8;
    private const int MaxScannedSchedules = 30;

    public CellOverrideProbeReport Run(View? activeView)
    {
        using var transaction = new Transaction(document, "Проверка переопределения ячеек спецификации");
        transaction.Start();

        try
        {
            return Probe(activeView);
        }
        finally
        {
            transaction.RollBack();
        }
    }

    private CellOverrideProbeReport Probe(View? activeView)
    {
        var schedule = SelectTarget(activeView);

        if (schedule is null)
        {
            return CreateFailure("Не найдено ни одной спецификации с русским текстом в ячейках.");
        }

        ViewSchedule duplicate;

        try
        {
            duplicate = (ViewSchedule)document.GetElement(schedule.Duplicate(ViewDuplicateOption.Duplicate));
        }
        catch (RevitException exception)
        {
            return CreateFailure($"Спецификация не дублируется: {exception.Message}", schedule.Name);
        }

        var wasOutOfDate = duplicate.IsDataOutOfDate();
        var rowsBeforeRefresh = ReadBody(duplicate).NumberOfRows;

        duplicate.RefreshData();
        document.Regenerate();

        var body = ReadBody(duplicate);

        return new CellOverrideProbeReport
        {
            ModelName = document.Title,
            CreatedAt = DateTime.Now,
            ScheduleName = schedule.Name,
            DataWasOutOfDate = wasOutOfDate,
            BodyRowsBeforeRefresh = rowsBeforeRefresh,
            BodyRowsAfterRefresh = body.NumberOfRows,
            BodyColumns = body.NumberOfColumns,
            ColumnHeadingError = TryWriteColumnHeading(duplicate),
            SampleCellTexts = ReadSampleTexts(body),
            Attempts = RunAttempts(schedule, duplicate),
            HeaderCellError = TryWriteHeaderCell(duplicate),
            BodyAllowsInsertRow = AllowsInsertRow(duplicate),
            InsertedRowError = TryWriteInsertedRow(duplicate)
        };
    }

    /// <summary>
    ///     Секция шапки не привязана к элементам: если она принимает текст, часть перевода можно держать в ней.
    /// </summary>
    private string? TryWriteHeaderCell(ViewSchedule duplicate)
    {
        try
        {
            var header = duplicate.GetTableData().GetSectionData(SectionType.Header);

            if (header.NumberOfRows is 0 || header.NumberOfColumns is 0)
            {
                return "Секция шапки пуста.";
            }

            header.SetCellText(0, 0, ProbeText);
            document.Regenerate();

            return null;
        }
        catch (RevitException exception)
        {
            return exception.Message;
        }
    }

    private static bool AllowsInsertRow(ViewSchedule duplicate)
    {
        try
        {
            var body = ReadBody(duplicate);
            return body.CanInsertRow(body.NumberOfRows);
        }
        catch (RevitException)
        {
            return false;
        }
    }

    /// <summary>
    ///     Вставленная строка не соответствует ни одному элементу: если она принимает текст, английскую
    ///     спецификацию можно собрать целиком из таких строк.
    /// </summary>
    private string? TryWriteInsertedRow(ViewSchedule duplicate)
    {
        try
        {
            var body = ReadBody(duplicate);
            var insertedRow = body.NumberOfRows;

            if (!body.CanInsertRow(insertedRow))
            {
                return "Revit не разрешает вставить строку в тело.";
            }

            body.InsertRow(insertedRow);
            document.Regenerate();

            ReadBody(duplicate).SetCellText(insertedRow, 0, ProbeText);
            document.Regenerate();

            var written = ReadCellText(duplicate, insertedRow, 0);

            if (!string.Equals(written, ProbeText, StringComparison.Ordinal))
            {
                return $"Строка вставлена, но текст не записался: в ячейке \"{written}\".";
            }

            return null;
        }
        catch (RevitException exception)
        {
            return exception.Message;
        }
    }

    private IReadOnlyList<CellOverrideProbeAttempt> RunAttempts(ViewSchedule source, ViewSchedule duplicate)
    {
        var attempts = new List<CellOverrideProbeAttempt>();

        // Нулевая строка тела - шапка. Проверять надо в первую очередь строки данных.
        var cells = FindRussianCells(ReadBody(duplicate))
            .OrderBy(cell => cell.Row is 0 ? 1 : 0)
            .Take(MaxAttempts);

        foreach (var (row, column) in cells)
        {
            attempts.Add(Attempt(source, duplicate, row, column));
        }

        return attempts;
    }

    private CellOverrideProbeAttempt Attempt(ViewSchedule source, ViewSchedule duplicate, int row, int column)
    {
        var textBefore = ReadCellText(duplicate, row, column);
        var sourceBefore = ReadCellText(source, row, column);

        string? error = null;

        try
        {
            ReadBody(duplicate).SetCellText(row, column, ProbeText);
            document.Regenerate();
        }
        catch (RevitException exception)
        {
            error = exception.Message;
        }

        var textAfter = ReadCellText(duplicate, row, column);
        var sourceAfter = ReadCellText(source, row, column);
        var isOverridden = ReadIsOverridden(duplicate, row, column);
        var sourceChanged = !string.Equals(sourceBefore, sourceAfter, StringComparison.Ordinal);

        return new CellOverrideProbeAttempt
        {
            Row = row,
            Column = column,
            TextBefore = textBefore,
            TextAfter = textAfter,
            IsCellOverridden = isOverridden,
            SourceScheduleChanged = sourceChanged,
            Error = error,
            Verdict = ResolveVerdict(error, textAfter, sourceChanged)
        };
    }

    private static CellOverrideVerdict ResolveVerdict(string? error, string textAfter, bool sourceChanged)
    {
        if (error is not null)
        {
            return CellOverrideVerdict.Rejected;
        }

        if (sourceChanged)
        {
            return CellOverrideVerdict.WrittenToModel;
        }

        if (string.Equals(textAfter, ProbeText, StringComparison.Ordinal))
        {
            return CellOverrideVerdict.OverriddenInCopy;
        }

        return CellOverrideVerdict.NoEffect;
    }

    /// <summary>
    ///     Заголовок колонки задаётся в определении спецификации, а не в таблице: проверяется отдельно.
    /// </summary>
    private static string? TryWriteColumnHeading(ViewSchedule duplicate)
    {
        try
        {
            var definition = duplicate.Definition;
            var fieldIds = definition.GetFieldOrder();

            if (fieldIds.Count is 0)
            {
                return "В спецификации нет полей.";
            }

            definition.GetField(fieldIds[0]).ColumnHeading = ProbeText;
            return null;
        }
        catch (RevitException exception)
        {
            return exception.Message;
        }
    }

    private ViewSchedule? SelectTarget(View? activeView)
    {
        if (activeView is ViewSchedule activeSchedule && IsProbeable(activeSchedule))
        {
            return activeSchedule;
        }

        var candidates = new FilteredElementCollector(document)
            .OfClass(typeof(ViewSchedule))
            .Cast<ViewSchedule>()
            .Where(IsProbeable)
            .OrderBy(schedule => schedule.Name, StringComparer.CurrentCulture)
            .Take(MaxScannedSchedules);

        foreach (var candidate in candidates)
        {
            candidate.RefreshData();

            if (FindRussianCells(ReadBody(candidate)).Any())
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool IsProbeable(ViewSchedule schedule)
    {
        if (schedule.IsTemplate)
        {
            return false;
        }

        if (schedule.IsTitleblockRevisionSchedule)
        {
            return false;
        }

        if (schedule.IsInternalKeynoteSchedule)
        {
            return false;
        }

        return !schedule.Definition.IsKeySchedule;
    }

    private static IEnumerable<(int Row, int Column)> FindRussianCells(TableSectionData body)
    {
        for (var row = 0; row < body.NumberOfRows; row++)
        {
            for (var column = 0; column < body.NumberOfColumns; column++)
            {
                if (RussianText.ContainsCyrillic(ReadCellText(body, row, column)))
                {
                    yield return (row, column);
                }
            }
        }
    }

    private static IReadOnlyList<string> ReadSampleTexts(TableSectionData body)
    {
        var samples = new List<string>();

        for (var row = 0; row < body.NumberOfRows && samples.Count < MaxSampleCells; row++)
        {
            for (var column = 0; column < body.NumberOfColumns && samples.Count < MaxSampleCells; column++)
            {
                var text = ReadCellText(body, row, column);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    samples.Add($"[{row};{column}] {text}");
                }
            }
        }

        return samples;
    }

    private static TableSectionData ReadBody(ViewSchedule schedule)
    {
        return schedule.GetTableData().GetSectionData(SectionType.Body);
    }

    private static string ReadCellText(ViewSchedule schedule, int row, int column)
    {
        return ReadCellText(ReadBody(schedule), row, column);
    }

    private static string ReadCellText(TableSectionData body, int row, int column)
    {
        if (!body.IsValidRowNumber(row) || !body.IsValidColumnNumber(column))
        {
            return string.Empty;
        }

        try
        {
            return body.GetCellText(row, column) ?? string.Empty;
        }
        catch (RevitException)
        {
            return string.Empty;
        }
    }

    private static bool ReadIsOverridden(ViewSchedule schedule, int row, int column)
    {
        try
        {
            return ReadBody(schedule).IsCellOverridden(row, column);
        }
        catch (RevitException)
        {
            return false;
        }
    }

    private CellOverrideProbeReport CreateFailure(string failure, string scheduleName = "")
    {
        return new CellOverrideProbeReport
        {
            ModelName = document.Title,
            CreatedAt = DateTime.Now,
            ScheduleName = scheduleName,
            DataWasOutOfDate = false,
            BodyRowsBeforeRefresh = 0,
            BodyRowsAfterRefresh = 0,
            BodyColumns = 0,
            BodyAllowsInsertRow = false,
            Attempts = [],
            SampleCellTexts = [],
            Failure = failure
        };
    }
}
