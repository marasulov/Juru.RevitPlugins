using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.ClashNavigator.Models.Clashes;
using Juru.RevitPlugins.ClashNavigator.Models.RevitClashReport.ServerClashes.Dto;
using Juru.RevitPlugins.Core.FileNaming;

namespace Juru.RevitPlugins.ClashNavigator.Models.RevitClashReport.ServerClashes;

/// <summary>
/// Загружает отчёт о коллизиях с сервера по имени файла Revit, разобранному по стандарту наименования организации.
/// </summary>
internal static class ServerReportLoader {
    private const string _vsSeparator = "-vs-";

    public static async Task<ReportModel> GetReportAsync(
        ClashServerClient client,
        RevitFileNameInfo fileInfo,
        RevitRepository revitRepository) {

        if(client is null) { throw new ArgumentNullException(nameof(client)); }
        if(fileInfo is null) { throw new ArgumentNullException(nameof(fileInfo)); }
        if(revitRepository is null) { throw new ArgumentNullException(nameof(revitRepository)); }

        var projects = await client.GetProjectsAsync().ConfigureAwait(false);
        var project = projects?.FirstOrDefault(p => NamesMatch(p.Name, fileInfo.ProjectCode));
        if(project is null) {
            throw new ArgumentException(
                $"На сервере не найден проект \"{fileInfo.ProjectCode}\". "
                + $"Проекты на сервере: {FormatNames(projects?.Select(p => p.Name))}.");
        }

        var objects = await client.GetObjectsAsync(project.Id).ConfigureAwait(false);
        var obj = objects?.FirstOrDefault(o => NamesMatch(o.Name, fileInfo.ObjectName));
        if(obj is null) {
            throw new ArgumentException(
                $"На сервере не найден объект \"{fileInfo.ObjectName}\" в проекте \"{fileInfo.ProjectCode}\". "
                + $"Объекты на сервере: {FormatNames(objects?.Select(o => o.Name))}.");
        }

        var clashes = await client.GetClashesAsync(obj.Id).ConfigureAwait(false);

        // В отчёт попадают все коллизии открытого файла - по дисциплине из его имени, независимо от того,
        // какие связи вставлены в модель. Незагруженная связь означает лишь, что такую коллизию
        // не получится показать в Ревите, но в списке и в работе со статусами она остаётся.
        string disciplineCode = fileInfo.Discipline.ToString();
        var clashModels = (clashes ?? [])
            .Where(c => MatchesDiscipline(c, disciplineCode))
            .Select(c => ToClashModel(c, client, revitRepository))
            .ToArray();

        string reportName = $"{fileInfo.ProjectCode}-{fileInfo.ObjectName}-{disciplineCode} (сервер)";
        return new ReportModel(reportName, clashModels);
    }

    /// <summary>
    /// Сравнивает имя из названия файла Revit с именем на сервере.
    /// В названиях файлов пробел записывают подчёркиванием, в базе сервера имя хранится с пробелами.
    /// </summary>
    private static bool NamesMatch(string serverName, string fileName) {
        return string.Equals(NormalizeName(serverName), NormalizeName(fileName), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeName(string name) {
        if(string.IsNullOrWhiteSpace(name)) {
            return string.Empty;
        }

        return string.Join(" ", name.Replace('_', ' ').Split([' '], StringSplitOptions.RemoveEmptyEntries));
    }

    private static string FormatNames(IEnumerable<string> names) {
        string joined = names is null ? string.Empty : string.Join(", ", names.Select(name => $"\"{name}\""));
        return string.IsNullOrEmpty(joined) ? "список пуст" : joined;
    }

    /// <summary>
    /// Относится ли коллизия к заданной дисциплине. Смотрим на дисциплины элементов, которые сообщает
    /// сервер; название теста разбираем только если их нет - оно соглашение, а не данные, и у теста
    /// с неканоническим именем дисциплину по нему не определить.
    /// </summary>
    private static bool MatchesDiscipline(ClashViewDto dto, string disciplineCode) {
        if(!string.IsNullOrWhiteSpace(dto.DisciplineA) || !string.IsNullOrWhiteSpace(dto.DisciplineB)) {
            return string.Equals(dto.DisciplineA, disciplineCode, StringComparison.OrdinalIgnoreCase)
                || string.Equals(dto.DisciplineB, disciplineCode, StringComparison.OrdinalIgnoreCase);
        }

        return ClashTestNameMatchesDiscipline(dto.ClashTestName, disciplineCode);
    }

    private static bool ClashTestNameMatchesDiscipline(string clashTestName, string disciplineCode) {
        if(string.IsNullOrWhiteSpace(clashTestName)) {
            return false;
        }

        return clashTestName
            .Split([_vsSeparator], StringSplitOptions.None)
            .Select(part => part.Split('_').FirstOrDefault())
            .Any(prefix => string.Equals(prefix, disciplineCode, StringComparison.OrdinalIgnoreCase));
    }

    private static ClashModel ToClashModel(ClashViewDto dto, ClashServerClient client, RevitRepository revitRepository) {
        var clash = new ClashModel {
            Name = dto.ClashTestName,
            ServerId = dto.Id,
            ClashStatus = ServerClashStatus.ToClashStatus(dto.Status),
            ServerStatus = dto.Status,
            StatusChangedBy = dto.StatusChangedBy,
            StatusChangedByName = dto.StatusChangedByName,
            StatusChangedAt = dto.StatusChangedAt,
            ClashPoint = ToClashPoint(dto),
            ImagePath = client.GetImageUrl(dto.ImagePath),
            MainElement = ToElementModel(dto.ObjectAName, dto.ElementIdA, dto.DisciplineA, revitRepository),
            OtherElement = ToElementModel(dto.ObjectBName, dto.ElementIdB, dto.DisciplineB, revitRepository)
        };
        return clash.SetRevitRepository(revitRepository);
    }

    private static ElementModel ToElementModel(
        string name, string elementIdString, string discipline, RevitRepository revitRepository) {

        var element = ResolveElementModel(name, elementIdString, discipline, revitRepository);
        // Имя объекта из базы показываем в таблице как есть, независимо от того,
        // удалось ли сопоставить элемент с моделью Revit.
        element.ObjectName = name ?? string.Empty;
        return element;
    }

    private static ElementModel ResolveElementModel(
        string name, string elementIdString, string discipline, RevitRepository revitRepository) {

        var id = BaseClashesLoader.ParseElementId(elementIdString);
        if(id == ElementId.InvalidElementId) {
            return ToElementModel(name, id, "<в отчёте нет Id>");
        }

        // Ищем элемент в файле его дисциплины, а не в первом попавшемся документе: Id элемента
        // уникален внутри файла, а не между файлами, и поиск "по всем" сопоставлял элемент не с тем -
        // например в коллизии дверей с помещениями второй элемент оказывался трубой из другого файла.
        var docInfos = GetDocInfos(revitRepository, discipline);
        if(docInfos.Count == 0) {
            return ToElementModel(name, id, $"<файл {discipline} не загружен>");
        }

        var match = docInfos
            .Select(info => new { Info = info, Element = info.Doc.GetElement(id) })
            .FirstOrDefault(item => item.Element is not null);
        if(match is null) {
            // Файл на месте, а элемента с таким Id в нём нет: модель изменилась после выгрузки
            // отчёта из Navisworks - элемент удалён или пересоздан.
            return ToElementModel(name, id, "<элемент не найден>");
        }

        // Уровень элемента не отображается в таблице навигатора коллизий с сервера,
        // а его вычисление (перебор параметров уровня) - основная часть времени сопоставления при больших отчётах.
        return new ElementModel(match.Element, new TransformModel(match.Info.Transform), computeLevel: false);
    }

    /// <summary>
    /// Документы, в которых имеет смысл искать элемент заданной дисциплины: открытый документ и связи,
    /// в имени которых есть код дисциплины. Если дисциплина не известна, возвращает все документы -
    /// тогда поиск идёт как раньше, по всем загруженным файлам.
    /// </summary>
    private static ICollection<DocInfo> GetDocInfos(RevitRepository revitRepository, string discipline) {
        if(string.IsNullOrWhiteSpace(discipline)) {
            return revitRepository.DocInfos;
        }

        var matching = revitRepository.DocInfos
            .Where(info => HasDiscipline(info.Name, discipline))
            .ToArray();
        return matching.Length > 0 ? matching : [];
    }

    /// <summary>
    /// Проверяет, что имя файла относится к заданной дисциплине. Имена приходят в разном виде -
    /// "BASH2-Dormitory_building-WSS-v24" у открытого документа и "BASH2-Dormitory_building-WSS_shared"
    /// у связи, - поэтому сравниваем по части имени между дефисами, отбрасывая суффикс после подчёркивания.
    /// </summary>
    private static bool HasDiscipline(string documentName, string discipline) {
        return (documentName ?? string.Empty)
            .Split('-')
            .Select(part => part.Split('_').First())
            .Any(part => string.Equals(part, discipline, StringComparison.OrdinalIgnoreCase));
    }

    private static XYZModel ToClashPoint(ClashViewDto dto) {
        if(!dto.CoordX.HasValue || !dto.CoordY.HasValue || !dto.CoordZ.HasValue) {
            return null;
        }

        return new XYZModel {
            X = (double) dto.CoordX.Value,
            Y = (double) dto.CoordY.Value,
            Z = (double) dto.CoordZ.Value
        };
    }

    /// <summary>
    /// Модель для элемента, который не удалось найти в загруженных документах.
    /// </summary>
    /// <param name="name">Имя объекта из отчёта</param>
    /// <param name="id">
    /// Id элемента из отчёта. Сохраняем как есть, даже когда элемент не найден: в таблице по нему видно,
    /// что отчёт этот Id знает. Раньше здесь всегда стоял InvalidElementId, и колонка Id показывала -1,
    /// будто отчёт пришёл без Id.
    /// </param>
    /// <param name="reason">
    /// Что показать в колонке файла вместо его имени: почему элемент не сопоставлен.
    /// </param>
    private static ElementModel ToElementModel(string name, ElementId id, string reason) {
        return new ElementModel {
            Id = id,
            Name = name ?? string.Empty,
            DocumentName = reason
        };
    }
}
