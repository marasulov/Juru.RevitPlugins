using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.ClashNavigator.Models;
using Juru.RevitPlugins.ClashNavigator.Models.Clashes;
using Juru.RevitPlugins.ClashNavigator.Models.Extensions;

namespace Juru.RevitPlugins.ClashNavigator.Services.Bcf;
/// <summary>
/// Коллизия, готовая к выгрузке в BCF.
/// </summary>
/// <param name="Title">Заголовок темы - имя коллизии и пара элементов.</param>
/// <param name="Status">Статус коллизии, как его показывает навигатор.</param>
/// <param name="Clash">Сама коллизия: элементы, комментарии.</param>
internal sealed record BcfClash(string Title, string Status, ClashModel Clash);

/// <summary>
/// Выгружает коллизии в BCF 2.1 - формат замечаний, который читают Revizto, BIMcollab,
/// Solibri, Navisworks и Autodesk Construction Cloud.
/// </summary>
/// <remarks>
/// <para>
/// Тема на коллизию: заголовок, статус, комментарии и точка обзора. В точке обзора оба элемента
/// перечислены как выбранные - по IFC GUID (так их находят программы, открывшие модель из IFC)
/// и по id Ревита (так - плагины внутри Ревита). Камера смотрит на габарит коллизии сверху
/// под углом.
/// </para>
/// <para>
/// Координаты камеры - внутренние координаты открытого файла в метрах. Они совпадают с моделью,
/// выгруженной в IFC во внутренних координатах; при выгрузке в общих координатах камера окажется
/// не там, но выбор элементов по GUID работает всегда, и программы наводятся по нему.
/// </para>
/// <para>
/// Снимков вида в темах нет: BCF 2.1 их не требует, а снимок на каждую коллизию - это
/// переключение вида и экспорт изображения, минуты на большом отчёте.
/// </para>
/// </remarks>
internal sealed class BcfExporter {
    private const double _feetToMeters = 0.3048;
    private const string _ifcGuidChars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";

    private readonly RevitRepository _revitRepository;
    private readonly string _author;

    public BcfExporter(RevitRepository revitRepository) {
        _revitRepository = revitRepository ?? throw new ArgumentNullException(nameof(revitRepository));
        _author = revitRepository.UiApplication.Application.Username;
    }

    /// <returns>Сколько тем записано.</returns>
    public int Export(IEnumerable<BcfClash> clashes, string path) {
        if(File.Exists(path)) {
            File.Delete(path);
        }

        int count = 0;
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Write(zip, "bcf.version", new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement("Version",
                new XAttribute("VersionId", "2.1"),
                new XElement("DetailedVersion", "2.1"))));

        foreach(var clash in clashes) {
            string topicGuid = Guid.NewGuid().ToString();
            string viewpointGuid = Guid.NewGuid().ToString();
            Write(zip, $"{topicGuid}/markup.bcf", CreateMarkup(clash, topicGuid, viewpointGuid));
            Write(zip, $"{topicGuid}/viewpoint.bcfv", CreateViewpoint(clash.Clash, viewpointGuid));
            count++;
        }
        return count;
    }

    private XDocument CreateMarkup(BcfClash clash, string topicGuid, string viewpointGuid) {
        var elements = new[] { clash.Clash.MainElement, clash.Clash.OtherElement };
        string description = string.Join(Environment.NewLine, elements
            .Where(item => item is not null)
            .Select(item => $"{item.DocumentName}: {item.Category} - {item.FamilyName} {item.Name} [{item.Id}]"));

        var markup = new XElement("Markup",
            new XElement("Topic",
                new XAttribute("Guid", topicGuid),
                new XAttribute("TopicType", "Clash"),
                new XAttribute("TopicStatus", clash.Status ?? string.Empty),
                new XElement("Title", clash.Title),
                new XElement("CreationDate", Date(DateTime.Now)),
                new XElement("CreationAuthor", _author),
                new XElement("Description", description)));

        foreach(var comment in clash.Clash.Comments ?? []) {
            markup.Add(new XElement("Comment",
                new XAttribute("Guid", Guid.NewGuid().ToString()),
                new XElement("Date", Date(comment.CreatedDate)),
                new XElement("Author", comment.Author),
                new XElement("Comment", comment.Body)));
        }

        markup.Add(new XElement("Viewpoints",
            new XAttribute("Guid", viewpointGuid),
            new XElement("Viewpoint", "viewpoint.bcfv")));
        return new XDocument(new XDeclaration("1.0", "UTF-8", null), markup);
    }

    private XDocument CreateViewpoint(ClashModel clash, string viewpointGuid) {
        var selection = new XElement("Selection");
        foreach(var model in new[] { clash.MainElement, clash.OtherElement }.Where(item => item is not null)) {
            var element = model.GetElement(_revitRepository.DocInfos);
            var component = new XElement("Component");
            if(element is not null && GetIfcGuid(element) is { } ifcGuid) {
                component.Add(new XAttribute("IfcGuid", ifcGuid));
            }
            component.Add(
                new XElement("OriginatingSystem", "Revit"),
                new XElement("AuthoringToolId", model.Id.ToString()));
            selection.Add(component);
        }

        var info = new XElement("VisualizationInfo",
            new XAttribute("Guid", viewpointGuid),
            new XElement("Components",
                selection,
                new XElement("Visibility", new XAttribute("DefaultVisibility", "true"))));

        if(_revitRepository.GetCommonBoundingBox([clash.MainElement, clash.OtherElement]) is { } box
           && box.IsNotEmpty()) {
            info.Add(CreateCamera(box));
        }
        return new XDocument(new XDeclaration("1.0", "UTF-8", null), info);
    }

    /// <summary>
    /// Перспективная камера: сверху под углом 45° с юго-запада, на расстоянии, при котором
    /// габарит коллизии виден целиком.
    /// </summary>
    private static XElement CreateCamera(BoundingBoxXYZ box) {
        var center = (box.Min + box.Max) / 2;
        double size = Math.Max((box.Max - box.Min).GetLength(), 3 / _feetToMeters);
        var direction = new XYZ(1, 1, -1).Normalize();
        var position = center - direction * size * 1.5;
        var right = direction.CrossProduct(XYZ.BasisZ).Normalize();
        var up = right.CrossProduct(direction).Normalize();

        return new XElement("PerspectiveCamera",
            Point("CameraViewPoint", position * _feetToMeters),
            Point("CameraDirection", direction),
            Point("CameraUpVector", up),
            new XElement("FieldOfView", "60"));
    }

    private static XElement Point(string name, XYZ point) {
        return new XElement(name,
            new XElement("X", Number(point.X)),
            new XElement("Y", Number(point.Y)),
            new XElement("Z", Number(point.Z)));
    }

    /// <summary>
    /// IFC GUID элемента: параметр "IfcGUID", если его уже записала выгрузка в IFC, иначе тот же
    /// GUID, что выгрузка назначит, - из <see cref="ExportUtils.GetExportId" />.
    /// </summary>
    private static string GetIfcGuid(Element element) {
        string stored = element.get_Parameter(BuiltInParameter.IFC_GUID)?.AsString();
        if(!string.IsNullOrWhiteSpace(stored)) {
            return stored;
        }
        try {
            return ToIfcGuid(ExportUtils.GetExportId(element.Document, element.Id));
        } catch(Autodesk.Revit.Exceptions.ApplicationException) {
            return null;
        }
    }

    /// <summary>
    /// Сжатие GUID в 22 символа по правилам IFC - тем же, что у выгрузки IFC из Ревита.
    /// </summary>
    internal static string ToIfcGuid(Guid guid) {
        byte[] bytes = guid.ToByteArray();
        uint data1 = BitConverter.ToUInt32(bytes, 0);
        ushort data2 = BitConverter.ToUInt16(bytes, 4);
        ushort data3 = BitConverter.ToUInt16(bytes, 6);

        uint[] parts = [
            data1 / 16777216,
            data1 % 16777216,
            (uint) (data2 * 256 + data3 / 256),
            (uint) (data3 % 256 * 65536 + bytes[8] * 256 + bytes[9]),
            (uint) (bytes[10] * 65536 + bytes[11] * 256 + bytes[12]),
            (uint) (bytes[13] * 65536 + bytes[14] * 256 + bytes[15])
        ];

        var result = new StringBuilder(22);
        Encode(result, parts[0], 2);
        for(int i = 1; i < parts.Length; i++) {
            Encode(result, parts[i], 4);
        }
        return result.ToString();
    }

    private static void Encode(StringBuilder result, uint value, int length) {
        char[] chars = new char[length];
        for(int i = length - 1; i >= 0; i--) {
            chars[i] = _ifcGuidChars[(int) (value % 64)];
            value /= 64;
        }
        result.Append(chars);
    }

    private static void Write(ZipArchive zip, string name, XDocument document) {
        var entry = zip.CreateEntry(name);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        document.Save(writer);
    }

    private static string Date(DateTime date) {
        return date.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static string Number(double value) {
        return value.ToString("0.######", CultureInfo.InvariantCulture);
    }
}
