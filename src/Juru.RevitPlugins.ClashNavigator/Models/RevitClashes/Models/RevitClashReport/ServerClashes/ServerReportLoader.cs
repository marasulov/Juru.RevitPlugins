using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.ClashNavigator.Models.Clashes;
using Juru.RevitPlugins.ClashNavigator.Models.RevitClashReport.ServerClashes.Dto;

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
        var project = projects?.FirstOrDefault(
            p => string.Equals(p.Name, fileInfo.ProjectCode, StringComparison.OrdinalIgnoreCase));
        if(project is null) {
            throw new ArgumentException($"На сервере не найден проект \"{fileInfo.ProjectCode}\".");
        }

        var objects = await client.GetObjectsAsync(project.Id).ConfigureAwait(false);
        var obj = objects?.FirstOrDefault(
            o => string.Equals(o.Name, fileInfo.ObjectName, StringComparison.OrdinalIgnoreCase));
        if(obj is null) {
            throw new ArgumentException(
                $"На сервере не найден объект \"{fileInfo.ObjectName}\" в проекте \"{fileInfo.ProjectCode}\".");
        }

        var clashes = await client.GetClashesAsync(obj.Id).ConfigureAwait(false);

        string disciplineCode = fileInfo.Discipline.ToString();
        var clashModels = (clashes ?? [])
            .Where(c => ClashTestNameMatchesDiscipline(c.ClashTestName, disciplineCode))
            .Select(c => ToClashModel(c, client, revitRepository))
            .ToArray();

        string reportName = $"{fileInfo.ProjectCode}-{fileInfo.ObjectName}-{disciplineCode} (сервер)";
        return new ReportModel(reportName, clashModels);
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
            ClashStatus = ToClashStatus(dto.Status),
            ClashPoint = ToClashPoint(dto),
            ImagePath = client.GetImageUrl(dto.ImagePath),
            MainElement = ToElementModel(dto.ObjectAName, dto.ElementIdA, revitRepository),
            OtherElement = ToElementModel(dto.ObjectBName, dto.ElementIdB, revitRepository)
        };
        return clash.SetRevitRepository(revitRepository);
    }

    private static ElementModel ToElementModel(string name, string elementIdString, RevitRepository revitRepository) {
        var id = BaseClashesLoader.ParseElementId(elementIdString);
        if(id == ElementId.InvalidElementId) {
            return ToElementModel(name);
        }

        // Сервер не сообщает файл-источник элемента, поэтому ищем элемент с этим Id
        // в активном документе и во всех загруженных связях.
        var match = revitRepository.DocInfos
            .Select(info => new { Info = info, Element = info.Doc.GetElement(id) })
            .FirstOrDefault(item => item.Element is not null);
        if(match is not null) {
            // Уровень элемента не отображается в таблице навигатора коллизий с сервера,
            // а его вычисление (перебор параметров уровня) - основная часть времени сопоставления при больших отчётах.
            return new ElementModel(match.Element, new TransformModel(match.Info.Transform), computeLevel: false);
        }

        return ToElementModel(name);
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

    private static ElementModel ToElementModel(string name) {
        return new ElementModel {
            Id = ElementId.InvalidElementId,
            Name = name ?? string.Empty,
            // Сервер не хранит Id элемента и файл-источник, поэтому подсветка/зум в Revit для таких коллизий недоступны.
            DocumentName = "<загружено с сервера>"
        };
    }

    private static ClashStatus ToClashStatus(string status) {
        return status?.ToLowerInvariant() switch {
            "new" or "active" => ClashStatus.Active,
            "approved" => ClashStatus.Analized,
            "resolved" => ClashStatus.Solved,
            _ => ClashStatus.Active
        };
    }
}
