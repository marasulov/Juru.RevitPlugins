using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Nice3point.Revit.Toolkit.Options;

namespace Juru.RevitPlugins.Openings.Models.Placement;

/// <summary>
/// Догружает семейства заданий на отверстия из папки эталонных семейств: когда семейства
/// в проекте нет вовсе и когда стоящее в проекте устарело - в нём нет параметров, которые
/// плагин обязан заполнить.
/// </summary>
/// <remarks>
/// Устаревшее семейство перезагружается только по факту неудачной записи параметра, а не сверкой
/// с эталоном на каждом задании: сверка лезла бы к сетевой папке на каждый экземпляр, а перезапись
/// типов без причины меняет пользователю проект.
/// </remarks>
public class OpeningFamilyLibrary {
    private readonly Document _doc;
    private readonly string _libraryPath;
    private readonly string _dialogTitle;

    /// <summary>
    /// Семейства, которые уже пробовали перезагрузить из-за нехватки параметра в этом запуске -
    /// не больше одной перезагрузки на семейство за раз, иначе первый же реально отсутствующий
    /// параметр (опечатка в имени, а не устаревшее семейство) перезагружал бы семейство заново
    /// на каждом следующем задании.
    /// </summary>
    private readonly HashSet<string> _reloadAttempted = new(StringComparer.CurrentCultureIgnoreCase);

    private readonly SortedSet<string> _updated = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly SortedSet<string> _loaded = new(StringComparer.CurrentCultureIgnoreCase);

    public OpeningFamilyLibrary(Document doc, string libraryPath, string dialogTitle) {
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _libraryPath = libraryPath;
        _dialogTitle = dialogTitle;
    }

    /// <summary>
    /// Семейства, обновлённые эталоном из-за нехватки параметров, - для отчёта.
    /// </summary>
    public IReadOnlyCollection<string> UpdatedFamilies => _updated;

    /// <summary>
    /// Семейства, которых в проекте не было и которые пришлось загрузить, - для отчёта.
    /// </summary>
    public IReadOnlyCollection<string> LoadedFamilies => _loaded;

    /// <summary>
    /// Загружает семейство, которого нет в проекте, и возвращает его типоразмер.
    /// </summary>
    public FamilySymbol LoadMissing(string familyName) {
        string message = $"Семейства {familyName} нет в проекте - загружаю эталон с сервера семейств:\n{{0}}";
        if(!LoadFamilyFile(familyName, message, out var family)) {
            return null;
        }

        _loaded.Add(familyName);
        return FindSymbol(family.Name);
    }

    /// <summary>
    /// Перезагружает уже стоящее в проекте семейство эталоном, перезаписывая значения его типов -
    /// у экземпляров, уже размещённых в проекте, появятся все параметры эталона без пересоздания.
    /// Вызывается только когда обычная запись параметра не удалась.
    /// </summary>
    /// <returns>True, если семейство перезагрузилось и параметры стоит записать заново.</returns>
    public bool ReloadOutdated(string familyName) {
        if(!_reloadAttempted.Add(familyName)) {
            return false;
        }

        string message = $"В семействе {familyName} не нашлось нужных параметров - семейство в проекте "
            + "не последней версии. Обновляю его эталоном с сервера семейств:\n{0}";
        if(!LoadFamilyFile(familyName, message, out _)) {
            return false;
        }

        _updated.Add(familyName);
        return true;
    }

    /// <summary>
    /// Возвращает типоразмер семейства, загруженного в проект, или null, если семейства в проекте нет.
    /// </summary>
    public FamilySymbol FindSymbol(string familyName) {
        return new FilteredElementCollector(_doc)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .FirstOrDefault(item => item.FamilyName.Equals(familyName, StringComparison.CurrentCultureIgnoreCase));
    }

    private bool LoadFamilyFile(string familyName, string messageFormat, out Family family) {
        family = null;
        if(string.IsNullOrWhiteSpace(_libraryPath)) {
            return false;
        }

        string familyPath = Path.Combine(_libraryPath, familyName + ".rfa");
        if(!File.Exists(familyPath)) {
            return false;
        }

        TaskDialog.Show(_dialogTitle, string.Format(messageFormat, familyPath));

        // FamilyLoadOptions() по умолчанию перезаписывает значения параметров уже существующих
        // типов - ровно то, что нужно и для впервые загружаемого семейства (перезаписывать нечего),
        // и для устаревшего (подхватит недостающие параметры на уже стоящих экземплярах).
        return _doc.LoadFamily(familyPath, new FamilyLoadOptions(), out family);
    }
}
