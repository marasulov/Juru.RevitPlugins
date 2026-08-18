using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

using Juru.RevitPlugins.Schedules.Models;

namespace Juru.RevitPlugins.Schedules.Services;

/// <summary>
///     Сохраняет разбор спецификаций: подробный JSON и заготовку глоссария в CSV для переводчика.
/// </summary>
public static class ScheduleAuditReportWriter
{
    private const char CsvSeparator = ';';

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    ///     Записывает отчёт в новую папку и возвращает путь к ней.
    /// </summary>
    public static string Write(ScheduleAuditReport report, IReadOnlyList<GlossarySeedEntry> glossarySeed)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        if (glossarySeed is null)
        {
            throw new ArgumentNullException(nameof(glossarySeed));
        }

        var reportDirectory = ReportLocation.Create("ScheduleAudit", report.ModelName, report.CreatedAt);

        File.WriteAllText(
            Path.Combine(reportDirectory, "schedules.json"),
            JsonSerializer.Serialize(report, JsonOptions),
            new UTF8Encoding(false));

        // Excel читает CSV как ANSI, пока в начале файла нет метки UTF-8.
        File.WriteAllText(
            Path.Combine(reportDirectory, "glossary.csv"),
            BuildGlossaryCsv(glossarySeed),
            new UTF8Encoding(true));

        return reportDirectory;
    }

    private static string BuildGlossaryCsv(IReadOnlyList<GlossarySeedEntry> glossarySeed)
    {
        var csv = new StringBuilder();

        AppendCsvRow(csv, "Исходный текст", "Перевод", "Тип", "Где встречается");

        foreach (var entry in glossarySeed)
        {
            AppendCsvRow(
                csv,
                entry.SourceText,
                string.Empty,
                DescribeOrigin(entry.Origin),
                string.Join(" | ", entry.Usages));
        }

        return csv.ToString();
    }

    private static string DescribeOrigin(GlossarySeedOrigin origin)
    {
        return origin switch
        {
            GlossarySeedOrigin.ColumnHeading => "Заголовок колонки",
            GlossarySeedOrigin.CellValue => "Значение ячейки",
            GlossarySeedOrigin.CombinedParameterLiteral => "Разделитель комбинированной колонки",
            GlossarySeedOrigin.FilterValue => "Значение фильтра",
            _ => origin.ToString()
        };
    }

    private static void AppendCsvRow(StringBuilder csv, params string[] cells)
    {
        csv.AppendLine(string.Join(CsvSeparator.ToString(), cells.Select(EscapeCsvCell)));
    }

    private static string EscapeCsvCell(string cell)
    {
        if (cell.IndexOfAny([CsvSeparator, '"', '\r', '\n']) < 0)
        {
            return cell;
        }

        return $"\"{cell.Replace("\"", "\"\"")}\"";
    }
}
