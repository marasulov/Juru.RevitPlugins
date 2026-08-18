using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Autodesk.Revit.DB;

using RevitException = Autodesk.Revit.Exceptions.ApplicationException;

namespace Juru.RevitPlugins.Schedules.Services;

/// <summary>
///     Забирает напечатанное содержимое спецификации через <c>ViewSchedule.Export</c>. Таблица вида отдаёт
///     текст далеко не всех ячеек, а экспорт выгружает то же, что уходит на печать.
/// </summary>
public sealed class ScheduleTextExporter
{
    private const string FieldDelimiter = "\t";
    private const char Qualifier = '"';

    static ScheduleTextExporter()
    {
        // Revit пишет файл в кодовой странице системы, а её поддержка вне .NET Framework подключается явно.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    ///     Возвращает строки таблицы по ячейкам либо <c>null</c>, если экспорт не удался. Метод пишет файл на
    ///     диск и не должен выполняться внутри транзакции.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<string>>? TryExport(
        ViewSchedule schedule,
        string workingDirectory,
        out string? warning)
    {
        warning = null;

        var fileName = $"{SanitizeFileName(schedule.Name)}.txt";
        var filePath = Path.Combine(workingDirectory, fileName);

        var options = new ViewScheduleExportOptions
        {
            ColumnHeaders = ExportColumnHeaders.OneRow,
            FieldDelimiter = FieldDelimiter,
            TextQualifier = ExportTextQualifier.DoubleQuote,
            Title = false,
            HeadersFootersBlanks = true
        };

        try
        {
            Directory.CreateDirectory(workingDirectory);
            schedule.Export(workingDirectory, fileName, options);

            return ParseRows(ReadText(filePath));
        }
        catch (RevitException exception)
        {
            warning = $"Экспорт не удался: {exception.Message}";
            return null;
        }
        catch (IOException exception)
        {
            warning = $"Файл экспорта недоступен: {exception.Message}";
            return null;
        }
    }

    private static string ReadText(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);

        if (HasUtf16Mark(bytes))
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (HasUtf8Mark(bytes))
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1251).GetString(bytes);
        }
    }

    private static bool HasUtf16Mark(byte[] bytes)
    {
        return bytes.Length >= 2 && bytes[0] is 0xFF && bytes[1] is 0xFE;
    }

    private static bool HasUtf8Mark(byte[] bytes)
    {
        return bytes.Length >= 3 && bytes[0] is 0xEF && bytes[1] is 0xBB && bytes[2] is 0xBF;
    }

    /// <summary>
    ///     Разбирает выгрузку посимвольно: ячейка с переносами строк заключена в кавычки и занимает несколько
    ///     физических строк файла.
    /// </summary>
    private static IReadOnlyList<IReadOnlyList<string>> ParseRows(string content)
    {
        var rows = new List<IReadOnlyList<string>>();
        var cells = new List<string>();
        var cell = new StringBuilder();
        var inQualifier = false;

        for (var index = 0; index < content.Length; index++)
        {
            var symbol = content[index];

            if (symbol == Qualifier)
            {
                if (inQualifier && index + 1 < content.Length && content[index + 1] == Qualifier)
                {
                    cell.Append(Qualifier);
                    index++;
                    continue;
                }

                inQualifier = !inQualifier;
                continue;
            }

            if (!inQualifier && symbol is '\t')
            {
                cells.Add(cell.ToString());
                cell.Clear();
                continue;
            }

            if (!inQualifier && symbol is '\r' or '\n')
            {
                if (symbol is '\r' && index + 1 < content.Length && content[index + 1] is '\n')
                {
                    index++;
                }

                cells.Add(cell.ToString());
                cell.Clear();
                rows.Add(cells.ToList());
                cells.Clear();
                continue;
            }

            cell.Append(symbol);
        }

        if (cell.Length > 0 || cells.Count > 0)
        {
            cells.Add(cell.ToString());
            rows.Add(cells.ToList());
        }

        return rows;
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
}
