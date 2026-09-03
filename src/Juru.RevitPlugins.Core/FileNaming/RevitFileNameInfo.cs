using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Juru.RevitPlugins.Core.FileNaming;

/// <summary>
/// Компоненты имени файла Revit, разобранные по стандарту наименования организации:
/// Проект-Название_объекта-Дисциплина(3 символа)-v24(версия ревита).rvt
/// </summary>
/// <remarks>
/// Версия ревита и хвост после дисциплины необязательны: связи других дисциплин обычно подгружают
/// копиями с постфиксом (`-ARC_shared`, `_detached`), а версия в имени есть не у всех файлов.
/// </remarks>
public sealed class RevitFileNameInfo {
    // Длину кода проекта не проверяем: кодов ровно в 5 символов стандарт не гарантирует
    // (на сервере есть и "BASH2", и "O&M", и "DEMO"). Существование проекта всё равно
    // проверяется по списку с сервера - см. ServerReportLoader.
    // Хвост после дисциплины отделён разделителем (`_shared`, ` (копия)`), чтобы `ARCHIVE`
    // не разобрался как дисциплина ARC с хвостом `HIVE`.
    private static readonly Regex _pattern = new(
        @"^(?<project>[^-]+)-(?<object>.+)-(?<discipline>ARC|STR|WSS|ELE|HVC|FAD)"
        + @"(?:-v(?<version>\d+))?(?<suffix>[ _-].*)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private RevitFileNameInfo(string projectCode, string objectName, Discipline discipline, int revitVersion) {
        ProjectCode = projectCode;
        ObjectName = objectName;
        Discipline = discipline;
        RevitVersion = revitVersion;
    }

    public string ProjectCode { get; }
    public string ObjectName { get; }
    public Discipline Discipline { get; }
    public int RevitVersion { get; }

    /// <summary>
    /// Пытается разобрать имя файла Revit по стандарту наименования организации.
    /// </summary>
    /// <param name="fileName">Имя файла или полный путь к файлу .rvt</param>
    /// <param name="info">Результат разбора, если имя файла соответствует стандарту</param>
    /// <returns>True, если имя файла соответствует стандарту, иначе False</returns>
    public static bool TryParse(string fileName, out RevitFileNameInfo info) {
        info = null;
        if(string.IsNullOrWhiteSpace(fileName)) {
            return false;
        }

        string name = Path.GetFileNameWithoutExtension(fileName);
        var match = _pattern.Match(name);
        if(!match.Success) {
            return false;
        }

        if(!Enum.TryParse(match.Groups["discipline"].Value, true, out Discipline discipline)) {
            return false;
        }

        // Версии в имени может не быть - тогда 0, её всё равно никто не спрашивает.
        int.TryParse(match.Groups["version"].Value, out int version);

        info = new RevitFileNameInfo(match.Groups["project"].Value, match.Groups["object"].Value, discipline, version);
        return true;
    }
}
