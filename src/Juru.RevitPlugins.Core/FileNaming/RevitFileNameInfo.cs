using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Juru.RevitPlugins.Core.FileNaming;

/// <summary>
/// Компоненты имени файла Revit, разобранные по стандарту наименования организации:
/// Проект-Название_объекта-Дисциплина(3 символа)-v24(версия ревита).rvt
/// </summary>
public sealed class RevitFileNameInfo {
    // Длину кода проекта не проверяем: кодов ровно в 5 символов стандарт не гарантирует
    // (на сервере есть и "BASH2", и "O&M", и "DEMO"). Существование проекта всё равно
    // проверяется по списку с сервера - см. ServerReportLoader.
    private static readonly Regex _pattern = new(
        @"^(?<project>[^-]+)-(?<object>.+)-(?<discipline>ARC|STR|WSS|ELE|HVC|FAD)-v(?<version>\d+)$",
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

        if(!int.TryParse(match.Groups["version"].Value, out int version)) {
            return false;
        }

        info = new RevitFileNameInfo(match.Groups["project"].Value, match.Groups["object"].Value, discipline, version);
        return true;
    }
}
