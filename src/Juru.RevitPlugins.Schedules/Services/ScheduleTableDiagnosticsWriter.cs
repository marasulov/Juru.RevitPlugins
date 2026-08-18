using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using Juru.RevitPlugins.Schedules.Models;

namespace Juru.RevitPlugins.Schedules.Services;

/// <summary>
///     Выгружает фактические размеры снятой таблицы в миллиметрах. Единицы стилей и размеров ячеек в API
///     Revit не документированы, и подгонять отрисовку без этих чисел нельзя.
/// </summary>
public static class ScheduleTableDiagnosticsWriter
{
    private const double MillimetersPerFoot = 304.8;
    private const double PixelsPerMillimeter = 96.0 / 25.4;
    private const int MaxReportedCells = 500;

    public static string Write(ScheduleTableSnapshot snapshot, int appliedViewScale, DateTime timestamp)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        var reportDirectory = ReportLocation.Create("DraftingTable", snapshot.ScheduleName, timestamp);

        return WriteTo(snapshot, appliedViewScale, reportDirectory);
    }

    /// <summary>
    ///     Записывает выгрузку в готовую папку под именем спецификации: так пакетный прогон складывает
    ///     все таблицы рядом.
    /// </summary>
    public static string WriteTo(ScheduleTableSnapshot snapshot, int appliedViewScale, string reportDirectory)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        var reportPath = Path.Combine(reportDirectory, $"{SanitizeFileName(snapshot.ScheduleName)}.txt");

        File.WriteAllText(reportPath, BuildReport(snapshot, appliedViewScale), new UTF8Encoding(true));

        return reportPath;
    }

    private static string SanitizeFileName(string scheduleName)
    {
        var sanitized = new StringBuilder(scheduleName.Length);

        foreach (var symbol in scheduleName)
        {
            sanitized.Append(Path.GetInvalidFileNameChars().Contains(symbol) ? '_' : symbol);
        }

        return sanitized.ToString();
    }

    private static string BuildReport(ScheduleTableSnapshot snapshot, int appliedViewScale)
    {
        var text = new StringBuilder();

        text.AppendLine($"Спецификация: {snapshot.ScheduleName}");
        text.AppendLine($"Масштаб чертёжного вида после установки: 1:{appliedViewScale}");
        text.AppendLine("Все размеры пересчитаны из значений API как футы -> миллиметры.");
        text.AppendLine();

        foreach (var section in snapshot.Sections)
        {
            text.AppendLine($"=== Секция {section.SectionName} ===");
            text.AppendLine($"Колонок: {section.ColumnWidths.Count}, строк: {section.RowHeights.Count}");
            text.AppendLine($"Ширина таблицы: {Millimeters(section.ColumnWidths.Sum())} мм");
            text.AppendLine($"Высота таблицы: {Millimeters(section.RowHeights.Sum())} мм");
            text.AppendLine();

            text.AppendLine("Ширины колонок, мм:");
            text.AppendLine("   " + string.Join("  ", section.ColumnWidths.Select(Millimeters)));
            text.AppendLine("Высоты строк, мм:");
            text.AppendLine("   " + string.Join("  ", section.RowHeights.Select(Millimeters)));
            text.AppendLine();

            text.AppendLine("Ячейки (TextSize в пикселях при 96 DPI и в миллиметрах):");

            foreach (var cell in section.Cells.Take(MaxReportedCells))
            {
                var span = cell.RowSpan > 1 || cell.ColumnSpan > 1
                    ? $" span {cell.RowSpan}x{cell.ColumnSpan}"
                    : string.Empty;

                text.AppendLine(
                    $"   [{cell.Row};{cell.Column}]{span} TextSize={cell.TextSize.ToString("0.######", CultureInfo.InvariantCulture)}"
                    + $" ({TextSizeMillimeters(cell.TextSize)} мм) {cell.Alignment}"
                    + $" len={cell.Text.Length}"
                    + $" borders(L{cell.Borders.Left} R{cell.Borders.Right} T{cell.Borders.Top} B{cell.Borders.Bottom})"
                    + $" \"{Shorten(cell.Text)}\"");
            }

            if (section.Cells.Count > MaxReportedCells)
            {
                text.AppendLine($"   ... ещё {section.Cells.Count - MaxReportedCells} ячеек");
            }

            text.AppendLine();
        }

        return text.ToString();
    }

    private static string TextSizeMillimeters(double pixels)
    {
        return (pixels / PixelsPerMillimeter).ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string Millimeters(double feet)
    {
        return (feet * MillimetersPerFoot).ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string Shorten(string text)
    {
        var singleLine = text.Replace("\r", " ").Replace("\n", " ");

        return singleLine.Length <= 45 ? singleLine : $"{singleLine[..45]}…";
    }
}
