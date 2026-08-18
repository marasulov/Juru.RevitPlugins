using System;
using System.IO;
using System.Linq;
using System.Text;

using Juru.RevitPlugins.Schedules.Models;

namespace Juru.RevitPlugins.Schedules.Services;

/// <summary>
///     Приводит результат зонда к виду, по которому принимается решение об архитектуре перевода.
/// </summary>
public static class CellOverrideProbeWriter
{
    public static string Write(CellOverrideProbeReport report)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        var reportDirectory = ReportLocation.Create("CellOverrideProbe", report.ModelName, report.CreatedAt);
        var reportPath = Path.Combine(reportDirectory, "probe.txt");

        File.WriteAllText(reportPath, BuildReport(report), new UTF8Encoding(true));

        return reportPath;
    }

    public static string BuildVerdict(CellOverrideProbeReport report)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        if (report.Failure is not null)
        {
            return report.Failure;
        }

        if (report.Attempts.Count is 0)
        {
            return "Не нашлось ни одной ячейки с русским текстом: проверить не на чем.";
        }

        if (report.Attempts.Any(attempt => attempt.Verdict is CellOverrideVerdict.WrittenToModel))
        {
            return "НЕТ. Запись в ячейку меняет модель — копирование текста неприменимо.";
        }

        if (report.Attempts.All(attempt => attempt.Verdict is CellOverrideVerdict.OverriddenInCopy))
        {
            return "ДА. Текст лёг поверх ячеек копии, модель не затронута.";
        }

        if (report.Attempts.All(attempt => attempt.Verdict is CellOverrideVerdict.Rejected))
        {
            if (report.InsertedRowError is null)
            {
                return "Ячейки данных не переопределяются, но вставленные строки принимают текст: статическая таблица возможна.";
            }

            return "НЕТ. Revit отклонил запись во все ячейки данных.";
        }

        return "Частично: результат по ячейкам разный, смотреть отчёт.";
    }

    private static string BuildReport(CellOverrideProbeReport report)
    {
        var text = new StringBuilder();

        text.AppendLine("Зонд: можно ли построить английскую спецификацию копированием текста");
        text.AppendLine(new string('=', 78));
        text.AppendLine($"Модель:         {report.ModelName}");
        text.AppendLine($"Спецификация:   {report.ScheduleName}");
        text.AppendLine($"Время:          {report.CreatedAt:yyyy-MM-dd HH:mm:ss}");
        text.AppendLine();
        text.AppendLine($"ВЕРДИКТ: {BuildVerdict(report)}");
        text.AppendLine();

        if (report.Failure is not null)
        {
            return text.ToString();
        }

        text.AppendLine("Чтение отрендеренного текста");
        text.AppendLine(new string('-', 78));
        text.AppendLine($"Данные были устаревшими:  {(report.DataWasOutOfDate ? "да" : "нет")}");
        text.AppendLine($"Строк до RefreshData:     {report.BodyRowsBeforeRefresh}");
        text.AppendLine($"Строк после RefreshData:  {report.BodyRowsAfterRefresh}");
        text.AppendLine($"Колонок:                  {report.BodyColumns}");
        text.AppendLine();
        text.AppendLine("Первые непустые ячейки:");
        foreach (var sample in report.SampleCellTexts)
        {
            text.AppendLine($"   {sample}");
        }

        text.AppendLine();
        text.AppendLine("Заголовок колонки");
        text.AppendLine(new string('-', 78));
        text.AppendLine(report.ColumnHeadingError is null
            ? "Записан без ошибок."
            : $"Ошибка: {report.ColumnHeadingError}");

        text.AppendLine();
        text.AppendLine("Обходные пути");
        text.AppendLine(new string('-', 78));
        text.AppendLine(report.HeaderCellError is null
            ? "Секция шапки: текст записан."
            : $"Секция шапки: {report.HeaderCellError}");
        text.AppendLine($"Вставка строки в тело разрешена: {(report.BodyAllowsInsertRow ? "да" : "нет")}");
        text.AppendLine(report.InsertedRowError is null
            ? "Вставленная строка: текст записан."
            : $"Вставленная строка: {report.InsertedRowError}");

        text.AppendLine();
        text.AppendLine("Запись в ячейки данных");
        text.AppendLine(new string('-', 78));

        foreach (var attempt in report.Attempts)
        {
            text.AppendLine($"Ячейка [{attempt.Row};{attempt.Column}] — {DescribeVerdict(attempt.Verdict)}");
            text.AppendLine($"   было:                    {attempt.TextBefore}");
            text.AppendLine($"   стало:                   {attempt.TextAfter}");
            text.AppendLine($"   помечена переопределённой: {(attempt.IsCellOverridden ? "да" : "нет")}");
            text.AppendLine($"   исходная спецификация изменилась: {(attempt.SourceScheduleChanged ? "ДА" : "нет")}");

            if (attempt.Error is not null)
            {
                text.AppendLine($"   ошибка: {attempt.Error}");
            }

            text.AppendLine();
        }

        return text.ToString();
    }

    private static string DescribeVerdict(CellOverrideVerdict verdict)
    {
        return verdict switch
        {
            CellOverrideVerdict.OverriddenInCopy => "переопределение в копии, модель цела",
            CellOverrideVerdict.WrittenToModel => "запись ушла в модель",
            CellOverrideVerdict.Rejected => "Revit отклонил запись",
            CellOverrideVerdict.NoEffect => "без эффекта",
            _ => verdict.ToString()
        };
    }
}
