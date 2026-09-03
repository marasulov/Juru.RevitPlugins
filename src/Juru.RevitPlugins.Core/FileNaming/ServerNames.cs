using System;

namespace Juru.RevitPlugins.Core.FileNaming;

/// <summary>
/// Сопоставление имён проекта и объекта из имени файла Revit с именами в базе сервера.
/// </summary>
/// <remarks>
/// В именах файлов пробел записывают подчёркиванием, а в базе сервера имя хранится с пробелами:
/// файл <c>BASH2-Dormitory_building-HVC.rvt</c> относится к объекту <c>Dormitory building</c>.
/// Правило одно на все плагины: отправить задания под именем с подчёркиванием - значит завести
/// на сервере второй объект-двойник, задания в котором не увидит ни фронт, ни соседний плагин.
/// </remarks>
public static class ServerNames {
    /// <summary>
    /// Одно ли это имя - без учёта регистра, подчёркиваний и повторных пробелов.
    /// </summary>
    public static bool Match(string serverName, string fileName) {
        return string.Equals(Normalize(serverName), Normalize(fileName), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Приводит имя к виду, в котором имена сравниваются: подчёркивания - пробелы, повторные
    /// пробелы схлопнуты, края обрезаны.
    /// </summary>
    public static string Normalize(string name) {
        if(string.IsNullOrWhiteSpace(name)) {
            return string.Empty;
        }

        return string.Join(" ", name.Replace('_', ' ').Split([' '], StringSplitOptions.RemoveEmptyEntries));
    }
}
