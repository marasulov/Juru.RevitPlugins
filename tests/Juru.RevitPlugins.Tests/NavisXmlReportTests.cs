using System.Linq;
using System.Xml.Linq;

using ClashNavigator.Models.RevitClashReport;

using Xunit;

namespace Juru.RevitPlugins.Tests;

/// <summary>
/// Разбор xml отчёта о коллизиях Navisworks: узлы коллизий, свойства объектов и статусы.
/// </summary>
public class NavisXmlReportTests {
    private const string _report = """
        <?xml version="1.0" encoding="UTF-8" ?>
        <exchange units="m">
          <batchtest name="Report">
            <clashtests>
              <clashtest name="ARC-MEP">
                <clashresults>
                  <clashresult name="Clash1" status="new">
                    <resultstatus>Создать</resultstatus>
                    <clashobjects>
                      <clashobject>
                        <objectattribute><name>Element ID</name><value>101</value></objectattribute>
                        <layer>Level 1</layer>
                        <smarttags>
                          <smarttag><name>Item Name</name><value>Basic Wall</value></smarttag>
                          <smarttag><name>Item Type</name><value>Wall 200</value></smarttag>
                          <smarttag><name>Item Source File</name><value>PRJ_ARC.rvt</value></smarttag>
                        </smarttags>
                      </clashobject>
                      <clashobject>
                        <smarttags>
                          <smarttag><name>Объект Id</name><value>202</value></smarttag>
                          <smarttag><name>Элемент Файл источника</name><value>PRJ_MEP.rvt</value></smarttag>
                          <smarttag><name>Объект Имя</name><value>Труба</value></smarttag>
                          <smarttag><name>Уровень</name><value>Этаж 1</value></smarttag>
                        </smarttags>
                      </clashobject>
                    </clashobjects>
                  </clashresult>
                  <clashgroup name="Group1">
                    <resultstatus>Утверждено</resultstatus>
                    <clashobjects>
                      <clashobject><layer>L2</layer></clashobject>
                    </clashobjects>
                  </clashgroup>
                  <clashgroup name="Group2" status="active">
                    <clashresults>
                      <clashresult name="Clash2" status="resolved" />
                      <clashresult name="Clash3">
                        <resultstatus>Исправлено</resultstatus>
                      </clashresult>
                    </clashresults>
                  </clashgroup>
                </clashresults>
              </clashtest>
            </clashtests>
          </batchtest>
        </exchange>
        """;

    private static XElement ClashTest() => XDocument.Parse(_report).Descendants("clashtest").Single();

    [Fact]
    public void GetClashResults_ReadsGroupsWithoutNestedResults() {
        string[] names = NavisXmlReport.GetClashResults(ClashTest())
            .Select(node => (string) node.Attribute("name"))
            .ToArray();

        Assert.Equal(["Clash1", "Group1", "Clash2", "Clash3"], names);
    }

    [Fact]
    public void GetPropertyValue_FindsEnglishObjectAttributesAndSmartTags() {
        var clashObject = ClashTest().Descendants("clashobject").First();

        Assert.Equal("101", NavisXmlReport.GetPropertyValue(clashObject, NavisXmlReport.ElementIdNames));
        Assert.Equal("PRJ_ARC.rvt", NavisXmlReport.GetPropertyValue(clashObject, NavisXmlReport.SourceFileNames));
        Assert.Equal("Basic Wall", NavisXmlReport.GetPropertyValue(clashObject, NavisXmlReport.ObjectNameNames));
        Assert.Equal("Wall 200", NavisXmlReport.GetPropertyValue(clashObject, NavisXmlReport.ObjectTypeNames));
        Assert.Equal("Level 1", NavisXmlReport.GetLevel(clashObject));
    }

    [Fact]
    public void GetPropertyValue_FindsRussianSmartTags() {
        var clashObject = ClashTest().Descendants("clashobject").ElementAt(1);

        Assert.Equal("202", NavisXmlReport.GetPropertyValue(clashObject, NavisXmlReport.ElementIdNames));
        Assert.Equal("PRJ_MEP.rvt", NavisXmlReport.GetPropertyValue(clashObject, NavisXmlReport.SourceFileNames));
        Assert.Equal("Труба", NavisXmlReport.GetPropertyValue(clashObject, NavisXmlReport.ObjectNameNames));
        Assert.Equal("Этаж 1", NavisXmlReport.GetLevel(clashObject));
    }

    [Fact]
    public void GetPropertyValue_ReturnsEmptyWhenObjectHasNoId() {
        var clashObject = ClashTest().Descendants("clashgroup").First().Descendants("clashobject").Single();

        Assert.Equal(string.Empty, NavisXmlReport.GetPropertyValue(clashObject, NavisXmlReport.ElementIdNames));
        Assert.Equal("L2", NavisXmlReport.GetLevel(clashObject));
    }

    [Fact]
    public void GetStatus_MapsAttributeAndLocalizedResultStatus() {
        string[] statuses = NavisXmlReport.GetClashResults(ClashTest())
            .Select(NavisXmlReport.GetStatus)
            .ToArray();

        Assert.Equal(["active", "approved", "resolved", "resolved"], statuses);
    }

    [Fact]
    public void GetStatus_ReturnsNullForUnknownStatus() {
        var clashResult = XElement.Parse("<clashresult status=\"???\"><resultstatus>Что-то</resultstatus></clashresult>");

        Assert.Null(NavisXmlReport.GetStatus(clashResult));
    }
}
