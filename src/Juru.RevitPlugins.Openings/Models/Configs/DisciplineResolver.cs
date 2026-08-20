using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Juru.RevitPlugins.Core.FileNaming;

namespace Juru.RevitPlugins.Openings.Models.Configs;

/// <summary>
/// Определяет дисциплину файла: сначала по стандарту наименования, затем по фрагменту имени.
/// </summary>
/// <remarks>
/// Стандарт `Проект-Объект-Дисциплина-vNN` соблюдён не во всех проектах: файлы вида `01_000_OV`
/// существуют и работать с ними надо. Поэтому в настройках лежит таблица соответствий фрагмента имени
/// и дисциплины, и она же нужна, чтобы узнать дисциплину связи.
/// </remarks>
public class DisciplineResolver {
    private readonly IReadOnlyCollection<DisciplineNameRule> _rules;

    public DisciplineResolver(OpeningsConfig config) {
        if(config is null) { throw new ArgumentNullException(nameof(config)); }

        _rules = config.DisciplineNameRules ?? [];
    }

    /// <summary>
    /// Определяет дисциплину по имени или пути файла.
    /// </summary>
    /// <param name="fileName">Имя файла или полный путь.</param>
    /// <param name="discipline">Найденная дисциплина.</param>
    /// <param name="byStandard">
    /// True, если имя разобрано по стандарту наименования; False, если дисциплина узнана по фрагменту.
    /// </param>
    public bool TryResolve(string fileName, out Discipline discipline, out bool byStandard) {
        discipline = default;
        byStandard = false;

        if(string.IsNullOrWhiteSpace(fileName)) {
            return false;
        }

        if(RevitFileNameInfo.TryParse(fileName, out var fileInfo)) {
            discipline = fileInfo.Discipline;
            byStandard = true;
            return true;
        }

        string name = Path.GetFileNameWithoutExtension(fileName);
        var rule = _rules.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.Fragment)
            && name.IndexOf(item.Fragment, StringComparison.CurrentCultureIgnoreCase) >= 0);
        if(rule is null) {
            return false;
        }

        discipline = rule.Discipline;
        return true;
    }
}

/// <summary>
/// Соответствие фрагмента имени файла и дисциплины - для файлов не по стандарту наименования.
/// </summary>
public class DisciplineNameRule {
    /// <summary>
    /// Фрагмент имени файла, например <c>_OV</c>.
    /// </summary>
    public string Fragment { get; set; }

    /// <summary>
    /// Дисциплина, которой соответствует фрагмент.
    /// </summary>
    public Discipline Discipline { get; set; }
}
