using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using Juru.RevitPlugins.Schedules.Models;

namespace Juru.RevitPlugins.Schedules.Services;

/// <summary>
///     Отчёт по пакетному копированию листовых спецификаций: что скопировано, какие колонки видны и где
///     копия разошлась с оригиналом.
/// </summary>
public static class SheetScheduleCopyReportWriter
{
    public static string Write(
        IReadOnlyList<SheetScheduleCopy> copies,
        string modelName,
        Glossary glossary,
        DateTime timestamp)
    {
        if (copies is null)
        {
            throw new ArgumentNullException(nameof(copies));
        }

        var reportDirectory = ReportLocation.Create("SheetScheduleCopy", modelName, timestamp);
        var reportPath = Path.Combine(reportDirectory, "copies.txt");

        File.WriteAllText(reportPath, BuildReport(copies, modelName, glossary, timestamp), new UTF8Encoding(true));

        return reportPath;
    }

    private static string BuildReport(
        IReadOnlyList<SheetScheduleCopy> copies,
        string modelName,
        Glossary glossary,
        DateTime timestamp)
    {
        var text = new StringBuilder();

        text.AppendLine("Копии листовых спецификаций в чертёжных видах");
        text.AppendLine(new string('=', 78));
        text.AppendLine($"Модель:         {modelName}");
        text.AppendLine($"Время:          {timestamp:yyyy-MM-dd HH:mm:ss}");
        text.AppendLine($"Спецификаций:   {copies.Count}");
        text.AppendLine($"Скопировано:    {copies.Count(copy => copy.Error is null)}");
        text.AppendLine($"Глоссарий:      {DescribeGlossary(glossary)}");
        text.AppendLine();

        AppendProblems(text, copies);

        foreach (var copy in copies)
        {
            AppendCopy(text, copy);
        }

        return text.ToString();
    }

    private static string DescribeGlossary(Glossary glossary)
    {
        if (glossary.SourcePath is null)
        {
            return "не найден, текст оставлен русским";
        }

        return $"{glossary.TranslationCount} строк, без перевода осталось {glossary.Misses.Count}";
    }

    private static void AppendProblems(StringBuilder text, IReadOnlyList<SheetScheduleCopy> copies)
    {
        var failed = copies.Where(copy => copy.Error is not null).ToList();
        var skipped = copies.Where(copy => copy.SkippedCellCount > 0).ToList();
        var split = copies.Where(copy => copy.IsSplit).ToList();
        var empty = copies.Where(copy => copy.Error is null && copy.TextNoteCount is 0).ToList();
        var stale = copies
            .Where(copy => copy.Error is null && copy.RowCount != copy.RowCountBeforeActivation)
            .ToList();
        var notActivated = copies.Where(copy => !copy.WasActivated).ToList();
        var exportFailed = copies
            .Where(copy => copy.Error is null && copy.ExportedRowCount is 0)
            .ToList();
        var notPlaced = copies
            .Where(copy => copy.Error is null && (copy.PlacementError is not null || copy.NotPlacedOnSheets.Count > 0))
            .ToList();

        if (failed.Count is 0 && skipped.Count is 0 && split.Count is 0
            && empty.Count is 0 && stale.Count is 0 && notActivated.Count is 0 && notPlaced.Count is 0
            && exportFailed.Count is 0)
        {
            return;
        }

        text.AppendLine("Требует внимания");
        text.AppendLine(new string('-', 78));

        foreach (var copy in failed)
        {
            text.AppendLine($"ОШИБКА  {copy.ScheduleName}: {copy.Error}");
        }

        foreach (var copy in skipped)
        {
            text.AppendLine($"ЯЧЕЙКИ  {copy.ScheduleName}: не размещено {copy.SkippedCellCount}");
        }

        foreach (var copy in split)
        {
            text.AppendLine($"СЕГМЕНТЫ {copy.ScheduleName}: разбита на сегменты, копия содержит таблицу целиком");
        }

        foreach (var copy in empty)
        {
            text.AppendLine($"ПУСТО   {copy.ScheduleName}: в таблице нет текста, копия пустая");
        }

        foreach (var copy in stale)
        {
            text.AppendLine(
                $"СТРОКИ  {copy.ScheduleName}: до активации вида {copy.RowCountBeforeActivation}, после {copy.RowCount}");
        }

        foreach (var copy in notActivated)
        {
            text.AppendLine($"ВИД     {copy.ScheduleName}: вид не удалось открыть, данные могли остаться устаревшими");
        }

        foreach (var copy in exportFailed)
        {
            text.AppendLine(
                $"ЭКСПОРТ {copy.ScheduleName}: {copy.ExportWarning ?? "нет строк, текст взят из таблицы вида"}");
        }

        foreach (var copy in notPlaced)
        {
            var reason = copy.PlacementError ?? $"лист размещения только один, пропущены: {string.Join(", ", copy.NotPlacedOnSheets)}";
            text.AppendLine($"ЛИСТ    {copy.ScheduleName}: {reason}");
        }

        text.AppendLine();
    }

    private static void AppendCopy(StringBuilder text, SheetScheduleCopy copy)
    {
        text.AppendLine($"### {copy.ScheduleName}");
        text.AppendLine($"    Листы:  {string.Join(", ", copy.SheetNumbers)}");

        if (copy.Error is not null)
        {
            text.AppendLine($"    ОШИБКА: {copy.Error}");
            text.AppendLine();
            return;
        }

        text.AppendLine($"    Вид:    {copy.ViewName}");
        text.AppendLine(
            $"    Таблица: {copy.VisibleColumnHeadings.Count} колонок, {copy.RowCount} строк "
            + $"(до активации {copy.RowCountBeforeActivation}), "
            + $"ширина {copy.TableWidthMillimeters.ToString("0.#", CultureInfo.InvariantCulture)} мм");
        text.AppendLine($"    Нарисовано: {copy.LineCount} линий, {copy.TextNoteCount} примечаний");
        text.AppendLine($"    Вставлена на лист: {copy.PlacedOnSheet ?? "нет"}");
        text.AppendLine($"    Строк из экспорта: {copy.ExportedRowCount}"
            + (copy.ExportWarning is null ? string.Empty : $" ({copy.ExportWarning})"));

        if (copy.ReplacedCopyCount > 0)
        {
            text.AppendLine($"    Удалено прежних копий: {copy.ReplacedCopyCount}");
        }
        text.AppendLine($"    Видимые колонки: {string.Join(" | ", copy.VisibleColumnHeadings)}");

        if (copy.HiddenFieldNames.Count > 0)
        {
            text.AppendLine($"    Скрытые поля:    {string.Join(" | ", copy.HiddenFieldNames)}");
        }

        text.AppendLine();
    }
}
