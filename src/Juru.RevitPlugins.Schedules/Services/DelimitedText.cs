using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Juru.RevitPlugins.Schedules.Services;

/// <summary>
///     Разбирает и собирает текст с разделителями. Ячейка с переносами строк берётся в кавычки и занимает
///     несколько физических строк файла, поэтому разбор идёт посимвольно, а не построчно.
/// </summary>
public static class DelimitedText
{
    private const char Qualifier = '"';

    public static IReadOnlyList<IReadOnlyList<string>> Parse(string content, char delimiter)
    {
        var rows = new List<IReadOnlyList<string>>();
        var cells = new List<string>();
        var cell = new StringBuilder();
        var inQualifier = false;

        for (var index = 0; index < content.Length; index++)
        {
            var symbol = content[index];

            if (symbol is Qualifier)
            {
                if (inQualifier && index + 1 < content.Length && content[index + 1] is Qualifier)
                {
                    cell.Append(Qualifier);
                    index++;
                    continue;
                }

                inQualifier = !inQualifier;
                continue;
            }

            if (!inQualifier && symbol == delimiter)
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

    public static string BuildRow(char delimiter, params string[] cells)
    {
        return string.Join(delimiter.ToString(), cells.Select(cell => Escape(cell, delimiter)));
    }

    private static string Escape(string cell, char delimiter)
    {
        if (cell.IndexOfAny([delimiter, Qualifier, '\r', '\n']) < 0)
        {
            return cell;
        }

        return $"{Qualifier}{cell.Replace("\"", "\"\"")}{Qualifier}";
    }
}
