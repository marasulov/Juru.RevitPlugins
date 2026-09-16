#if DEBUG
using System;
using System.IO;
using System.Text;

namespace Juru.RevitPlugins.Schedules.Services;

/// <summary>
///     Кладёт исключение целиком в файл и возвращает путь к нему.
/// </summary>
/// <remarks>
///     Только для отладочной сборки. <c>TaskDialog</c> обрезает длинный текст, а у исключений из
///     недр WPF и Ревита весь смысл в нижних кадрах стека - именно они называют виновника, тогда
///     как верхние у любого такого сбоя одинаковы. Читать их из диалога невозможно, поэтому стек
///     пишется на диск.
/// </remarks>
public static class CrashLog
{
    public static string Write(Exception exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "JuruPlugins",
                "Crash");

            Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, $"Schedules_{DateTime.Now:yyyyMMdd_HHmmss_fff}.txt");

            File.WriteAllText(path, exception.ToString(), new UTF8Encoding(true));

            return path;
        }
        catch (IOException failure)
        {
            return $"Стек записать не удалось: {failure.Message}";
        }
    }
}
#endif
