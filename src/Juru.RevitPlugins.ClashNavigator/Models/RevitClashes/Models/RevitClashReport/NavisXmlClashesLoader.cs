using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

using Autodesk.Revit.DB;

using dosymep.Revit;

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

    public NavisXmlClashesLoader(RevitRepository revitRepository, string path) {
        if(string.IsNullOrWhiteSpace(path)) {
            throw new ArgumentException(nameof(path));
        }
        _revitRepository = revitRepository ?? throw new ArgumentNullException(nameof(revitRepository));
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
        return clashTest.Descendants("clashresult")
            .Select(GetClashModel)
            .Where(c => (c.MainElement?.Id.IsNotNull() ?? false) && (c.OtherElement?.Id.IsNotNull() ?? false))
            .ToArray();
    }

    private ClashModel GetClashModel(XElement clashResult) {
        string name = clashResult.Attribute("name")?.Value ?? string.Empty;
        var status = GetClashStatus(clashResult.Attribute("status")?.Value);
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
        if(elements.Length == 2) {
            clash.MainElement = elements[0];
            clash.OtherElement = elements[1];
        }
        return clash.SetRevitRepository(_revitRepository);
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
        return GetElementModelFromNavisworks(clashObject);

#pragma warning disable CS0162 // Legacy SmartTag parser kept for older exported reports.
        var tags = clashObject.Descendants("smarttag").ToDictionary(
            t => t.Element("name").Value.ToLower(),
            t => t.Element("value").Value);
        string file = tags.TryGetValue("элемент файл источника", out string fileName) ? fileName : null;
        const string rvtEnd = ".rvt";
        if(file.EndsWith(rvtEnd)) {
            file = file.Substring(0, file.Length - rvtEnd.Length);
        }
        var id = tags.TryGetValue("объект id", out string idStr) ? GetId(idStr) : ElementId.InvalidElementId;
        var elementModel = GetElementModel(file, id);
        return elementModel.Id.IsNotNull() ? elementModel :
            new ElementModel() {
                Id = id,
                DocumentName = file,
                Name = tags.TryGetValue("объект имя", out string name) ? name : string.Empty,
                FamilyName = tags.TryGetValue("объект семейство", out string famName) ? famName : string.Empty,
                Level = clashObject.Element("layer")?.Value ?? string.Empty
            };
#pragma warning restore CS0162
    }

    private ElementModel GetElementModelFromNavisworks(XElement clashObject) {
        var elementModel = ResolveElementModelFromNavisworks(clashObject);
        // Имя объекта из отчёта показываем в таблице как есть, независимо от того,
        // удалось ли сопоставить элемент с моделью Revit.
        elementModel.ObjectName = GetSmartTagValue(clashObject, "Item Name");
        return elementModel;
    }

    private ElementModel ResolveElementModelFromNavisworks(XElement clashObject) {
        var idValue = clashObject.Elements("objectattribute")
            .FirstOrDefault(attribute => string.Equals(
                (string)attribute.Element("name"), "Element ID", StringComparison.OrdinalIgnoreCase))
            ?.Element("value")?.Value;
        var id = GetId(idValue);
        if(id == ElementId.InvalidElementId) return new ElementModel { Id = id };

        // This Navisworks export does not identify the source RVT. Search the
        // active document and every loaded Revit link for the exported ID.
        var match = _revitRepository.DocInfos
            .Select(info => new { Info = info, Element = info.Doc.GetElement(id) })
            .FirstOrDefault(item => item.Element is not null);
        if(match is not null) return new ElementModel(match.Element, match.Info.Transform);

        return new ElementModel {
            Id = id,
            // The current Navisworks XML format exports no source-RVT field.
            // Keep the missing resolution visible instead of rendering a blank cell.
            DocumentName = "<\u043d\u0435 \u0437\u0430\u0433\u0440\u0443\u0436\u0435\u043d \u0432 Revit>",
            Name = GetSmartTagValue(clashObject, "Item Name"),
            FamilyName = GetSmartTagValue(clashObject, "Item Type"),
            Level = clashObject.Element("layer")?.Value ?? string.Empty
        };
    }

    private static string GetSmartTagValue(XElement clashObject, string name) =>
        clashObject.Descendants("smarttag")
            .FirstOrDefault(tag => string.Equals((string)tag.Element("name"), name, StringComparison.OrdinalIgnoreCase))
            ?.Element("value")?.Value ?? string.Empty;

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

    private ElementModel GetElementModel(string fileName, ElementId id) {
        var el = _revitRepository.GetElement(fileName, id);
        if(el != null) {
            var elModel = new ElementModel(el);
            var docInfo = elModel.GetDocInfo(_revitRepository.DocInfos);
            elModel.TransformModel = new TransformModel(docInfo.Transform);
            return elModel;
        } else {
            return new ElementModel() { Id = ElementId.InvalidElementId };
        }
    }

    private ClashStatus GetClashStatus(string status) {
        return (status?.ToLower()) switch {
            "new" or "active" => ClashStatus.Active,
            "reviewed" or "approved" => ClashStatus.Analized,
            "resolved" => ClashStatus.Solved,
            _ => ClashStatus.Active,
        };
    }
}
