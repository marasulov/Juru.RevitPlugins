using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

using Autodesk.Revit.DB;

using dosymep.Revit;
using dosymep.SimpleServices;

using Juru.RevitPlugins.ClashNavigator.Models.Clashes;
using Juru.RevitPlugins.ClashNavigator.Models.Interfaces;

namespace Juru.RevitPlugins.ClashNavigator.Models.RevitClashReport;
/// <summary>
/// Данный класс содержит логику по парсингу xml файла отчета о коллизиях из Navisworks.
/// </summary>

// Пример корректного xml файла, формат которого обрабатывается:
//
//<?xml version="1.0" encoding="UTF-8" ?>

//<exchange xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" units="m" filename="" filepath="">
//  <batchtest name="Report" internal_name="Report" units="m">
//    <clashtests>
//      <clashtest name="01_АР-АР_(Кладка)" test_type="hard" status="ok" tolerance="0.050" merge_composites="1">
//        <linkage mode="none"/>
//        <left>
//          <clashselection selfintersect="0" primtypes="1">
//            <locator>lcop_selection_set_tree/Этапы и подэтапы/АР/АР_Кладка</locator>
//          </clashselection>
//        </left>
//        <right>
//          <clashselection selfintersect="0" primtypes="1">
//            <locator>lcop_selection_set_tree/Этапы и подэтапы/АР/АР_Кладка</locator>
//          </clashselection>
//        </right>
//        <rules/>
//        <summary total="86" new="86" active="0" reviewed="0" approved="0" resolved="0">
//          <testtype>По пересечению</testtype>
//          <teststatus>ОК</teststatus>
//        </summary>
//        <clashresults>
//          <clashresult name="Конфликт1" guid="2d5094c6-5a33-46b8-9e23-10e541012ad6" href="AAAA-00_BB_CC_files\cd000001.jpg" status="new" distance="-0.132">
//            <resultstatus>Создать</resultstatus>
//            <createddate>
//              <date year="2025" month="3" day="14" hour="6" minute="50" second="20"/>
//            </createddate>
//            <clashobjects>
//              <clashobject>
//                <layer>06 этаж</layer>
//                <smarttags>
//                  <smarttag>
//                    <name>Элемент Файл источника</name>
//                    <value>AAAA-00_BB_CC_000.rvt</value>
//                  </smarttag>
//                  <smarttag>
//                    <name>Объект Id</name>
//                    <value>12645702</value>
//                  </smarttag>
//                  <smarttag>
//                    <name>Объект Семейство</name>
//                    <value>Базовая стена</value>
//                  </smarttag>
//                  <smarttag>
//                    <name>Объект Имя</name>
//                    <value>(Н) СН-1.1 ГБ-200</value>
//                  </smarttag>
//                </smarttags>
//              </clashobject>
//              <clashobject>
//                <layer>06 этаж</layer>
//                <smarttags>
//                  <smarttag>
//                    <name>Элемент Файл источника</name>
//                    <value>AAAA-00_BB_CC_001.rvt</value>
//                  </smarttag>
//                  <smarttag>
//                    <name>Объект Id</name>
//                    <value>12645451</value>
//                  </smarttag>
//                  <smarttag>
//                    <name>Объект Семейство</name>
//                    <value>Базовая стена</value>
//                  </smarttag>
//                  <smarttag>
//                    <name>Объект Имя</name>
//                    <value>(Н) СН-1.1 ГБ-200</value>
//                  </smarttag>
//                </smarttags>
//              </clashobject>
//            </clashobjects>
//          </clashresult>
//        </clashresults>
//      </clashtest>
//      </clashtests>
//    <selectionsets/>
//  </batchtest>
//</exchange>
internal class NavisXmlClashesLoader : BaseClashesLoader, IClashesLoader {
    private readonly RevitRepository _revitRepository;
    private readonly ILocalizationService _localization;

    /// <param name="localization">
    /// Служба локализации для описаний несопоставленных объектов. Если не задана,
    /// вместо описания в колонке файла показывается ключ строки.
    /// </param>
    public NavisXmlClashesLoader(
        RevitRepository revitRepository, string path, ILocalizationService localization = null) {
        if(string.IsNullOrWhiteSpace(path)) {
            throw new ArgumentException(nameof(path));
        }
        _revitRepository = revitRepository ?? throw new ArgumentNullException(nameof(revitRepository));
        _localization = localization;
        FilePath = path;
    }

    public string FilePath { get; }

    public IEnumerable<ReportModel> GetReports() {
        if(string.IsNullOrWhiteSpace(FilePath)) {
            throw new ArgumentException(nameof(FilePath));
        }

        if(!File.Exists(FilePath)) {
            throw new ArgumentException($"Файл \"{FilePath}\" отсутствует.", nameof(FilePath));
        }

        var xmlDoc = XDocument.Load(FilePath);

        return xmlDoc.Descendants("clashtest")
            .Select(ct => new ReportModel((string) ct.Attribute("name"), GetClashModels(ct)));
    }

    public bool IsValid() {
        return !string.IsNullOrEmpty(FilePath)
            && FilePath.EndsWith(".xml");
    }


    private ICollection<ClashModel> GetClashModels(XElement clashTest) {
        // Коллизии с несопоставленными объектами не отбрасываем: у нативной геометрии
        // Id элемента Revit в отчёте нет, и раньше такие коллизии пропадали из таблицы.
        // Причину, по которой такую коллизию нельзя показать в Ревите, объясняет
        // ClashViewModel.RevitHint по описанию в DocumentName элемента.
        return NavisXmlReport.GetClashResults(clashTest)
            .Select(GetClashModel)
            .ToArray();
    }

    private ClashModel GetClashModel(XElement clashResult) {
        string name = clashResult.Attribute("name")?.Value ?? string.Empty;
        var status = GetClashStatus(NavisXmlReport.GetStatus(clashResult));
        var elements = clashResult.Descendants("clashobject")
            .Take(2)
            .Select(GetElementModel)
            .ToArray();
        var comments = clashResult.Descendants("comment")
            .Select(GetClashComment)
            .ToHashSet();
        var clash = new ClashModel() {
            Name = name,
            ClashStatus = status,
            Comments = comments,
            ClashPoint = GetClashPoint(clashResult),
            ImagePath = GetImagePath(clashResult)
        };
        // Элементы назначаем всегда: ClashViewModel обращается к ним без проверки на null.
        clash.MainElement = elements.ElementAtOrDefault(0) ?? GetMissingElementModel();
        clash.OtherElement = elements.ElementAtOrDefault(1) ?? GetMissingElementModel();
        return clash.SetRevitRepository(_revitRepository);
    }

    /// <summary>
    /// Заглушка для объекта, которого в коллизии отчёта нет вовсе.
    /// </summary>
    private ElementModel GetMissingElementModel() {
        return new ElementModel() {
            Id = ElementId.InvalidElementId,
            DocumentName = GetText("NavisXml.ObjectMissing")
        };
    }

    private string GetImagePath(XElement clashResult) {
        string href = (string)clashResult.Attribute("href");
        if(string.IsNullOrWhiteSpace(href)) return null;

        string reportDirectory = Path.GetDirectoryName(FilePath);
        string imageName = Path.GetFileName(href);
        string directPath = Path.Combine(reportDirectory, href.Replace('/', Path.DirectorySeparatorChar));
        if(File.Exists(directPath)) return directPath;

        string reportName = Path.GetFileNameWithoutExtension(FilePath);
        string[] folders = [reportName + "_files", reportName + "__files"];
        return folders.Select(folder => Path.Combine(reportDirectory, folder, imageName))
            .FirstOrDefault(File.Exists);
    }

    private ElementModel GetElementModel(XElement clashObject) {
        string objectName = NavisXmlReport.GetPropertyValue(clashObject, NavisXmlReport.ObjectNameNames);
        var elementModel = ResolveElementModel(clashObject, objectName);
        // Имя объекта из отчёта показываем в таблице как есть, независимо от того,
        // удалось ли сопоставить элемент с моделью Revit.
        elementModel.ObjectName = objectName;
        return elementModel;
    }

    private ElementModel ResolveElementModel(XElement clashObject, string objectName) {
        var id = GetId(NavisXmlReport.GetPropertyValue(clashObject, NavisXmlReport.ElementIdNames));
        string sourceFile = NavisXmlReport.GetPropertyValue(clashObject, NavisXmlReport.SourceFileNames);
        var candidates = GetCandidateDocInfos(sourceFile);
        if(id.IsNotNull()) {
            var match = candidates
                .Select(info => new { Info = info, Element = info.Doc.GetElement(id) })
                .FirstOrDefault(item => item.Element is not null);
            if(match is not null) {
                return new ElementModel(match.Element, match.Info.Transform);
            }
        }

        return new ElementModel {
            Id = id,
            // Пустая ячейка файла выглядела бы как ошибка плагина, поэтому в ней - причина,
            // по которой объект не сопоставлен. Её же показывает подсказка ClashViewModel.RevitHint.
            DocumentName = GetUnresolvedDescription(id, sourceFile, candidates.Count > 0),
            Name = objectName,
            FamilyName = NavisXmlReport.GetPropertyValue(clashObject, NavisXmlReport.ObjectTypeNames),
            Level = NavisXmlReport.GetLevel(clashObject)
        };
    }

    /// <summary>
    /// Документы, в которых ищется элемент объекта отчёта.
    /// </summary>
    /// <remarks>
    /// Файл источника есть не во всех форматах отчёта. Если он не указан, перебираем активный
    /// документ и все связи. Если указан, ищем только в нём: Id элемента уникален лишь внутри
    /// одного файла, и в чужом документе тот же Id почти всегда находит посторонний элемент.
    /// Файл указан, но не загружен - элемент остаётся несопоставленным, а не подменяется.
    /// Дублирующиеся экземпляры одной связи остаются в выборке все: элемент берётся из первого,
    /// а трансформация хранится в модели элемента, как и раньше.
    /// </remarks>
    private ICollection<DocInfo> GetCandidateDocInfos(string sourceFile) {
        var docInfos = _revitRepository.DocInfos;
        return string.IsNullOrWhiteSpace(sourceFile)
            ? docInfos
            : docInfos.Where(info => IsSameDocument(info.Name, sourceFile)).ToArray();
    }

    /// <summary>Сравнивает имя документа Revit с именем файла из отчёта.</summary>
    private static bool IsSameDocument(string docName, string reportFileName) {
        // Имя из отчёта приводится так же, как имя документа (без суффикса пользователя
        // локальной копии и пути), иначе локальная копия не узнала бы свой же центральный файл.
        string reportName = RevitRepository.GetDocumentName(Path.GetFileNameWithoutExtension(reportFileName.Trim()));
        return string.Equals(docName, reportName, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>
    /// Возвращает описание причины, по которой объект отчёта не сопоставлен с элементом Revit.
    /// </summary>
    /// <param name="id">Id элемента из отчёта.</param>
    /// <param name="sourceFile">Файл источника из отчёта или пустая строка.</param>
    /// <param name="documentLoaded">Документ, в котором искался элемент, загружен в Revit.</param>
    private string GetUnresolvedDescription(ElementId id, string sourceFile, bool documentLoaded) {
        if(!id.IsNotNull()) {
            // Нативная геометрия и узлы групп Navisworks экспортируются без Id элемента Revit.
            return GetText("NavisXml.NoElementId");
        }
        if(string.IsNullOrWhiteSpace(sourceFile)) {
            return GetText("NavisXml.NotLoaded");
        }
        // Файл на месте, а элемента с таким Id в нём нет: модель изменилась после выгрузки
        // отчёта из Navisworks - элемент удалён или пересоздан.
        return documentLoaded
            ? GetText("NavisXml.ElementNotFound", sourceFile)
            : GetText("NavisXml.FileNotLoaded", sourceFile);
    }

    private string GetText(string key, params object[] args) {
        string text = _localization?.GetLocalizedString(key, args);
        return string.IsNullOrEmpty(text) ? key : text;
    }

    private static XYZModel GetClashPoint(XElement clashResult) {
        var point = clashResult.Element("clashpoint")?.Element("pos3f");
        if(point is null) return null;
        return new XYZModel {
            X = (double?)point.Attribute("x") ?? 0,
            Y = (double?)point.Attribute("y") ?? 0,
            Z = (double?)point.Attribute("z") ?? 0
        };
    }

    private ClashComment GetClashComment(XElement comment) {
        if(comment == null) {
            throw new ArgumentNullException(nameof(comment));
        }

        int id = (int?) comment.Attribute("id") ?? 0;
        string author = (string) comment.Element("user") ?? string.Empty;
        string body = (string) comment.Element("body") ?? string.Empty;
        DateTime date = GetDate(comment.Element("createddate"));

        return new ClashComment {
            Id = id,
            Author = author,
            Body = body,
            CreatedDate = date
        };
    }

    private DateTime GetDate(XElement createddate) {
        XElement dateElement = createddate?.Element("date");

        if(dateElement == null) {
            return DateTime.Now;
        }

        int year = (int?) dateElement.Attribute("year") ?? 0;
        int month = (int?) dateElement.Attribute("month") ?? 0;
        int day = (int?) dateElement.Attribute("day") ?? 0;
        int hour = (int?) dateElement.Attribute("hour") ?? 0;
        int minute = (int?) dateElement.Attribute("minute") ?? 0;
        int second = (int?) dateElement.Attribute("second") ?? 0;

        if(year <= 0) {
            return DateTime.Now;
        }

        return new DateTime(
            year,
            month,
            day,
            hour,
            minute,
            second,
            DateTimeKind.Local
        );
    }

    /// <param name="status">
    /// Статус Navisworks, уже приведённый к английскому (<see cref="NavisXmlReport.GetStatus"/>).
    /// </param>
    private static ClashStatus GetClashStatus(string status) {
        return (status?.ToLower()) switch {
            "new" or "active" => ClashStatus.Active,
            "reviewed" or "approved" => ClashStatus.Analized,
            "resolved" => ClashStatus.Solved,
            _ => ClashStatus.Active,
        };
    }
}
