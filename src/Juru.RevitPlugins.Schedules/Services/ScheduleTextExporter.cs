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
    private const char FieldDelimiter = '\t';

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
            FieldDelimiter = FieldDelimiter.ToString(),
            TextQualifier = ExportTextQualifier.DoubleQuote,
            Title = false,
            HeadersFootersBlanks = true
        };

        try
        {
            Directory.CreateDirectory(workingDirectory);
            schedule.Export(workingDirectory, fileName, options);

            return DelimitedText.Parse(ReadText(filePath), FieldDelimiter);
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
