using System.Linq;
using System.Xml.Linq;

using ClashNavigator.Services.NavisworksImport;

using Xunit;

namespace Juru.RevitPlugins.Tests;

/// <summary>
/// Разбор файлов обмена Navisworks: поисковые наборы и тесты Clash Detective.
/// </summary>
/// <remarks>
/// Образцы повторяют реальные выгрузки Navisworks Manage 2026 (папка проекта Samarkand):
/// ИЛИ-группы на двух языках, вложенные папки наборов, тест без левой стороны.
/// </remarks>
public class NavisworksXmlTests {
    private const string _searchSets = """
        <?xml version="1.0" encoding="UTF-8" ?>
        <exchange units="ft" filename="" filepath="">
          <selectionsets>
            <viewfolder name="ARC">
              <selectionset name="ARC_Walls">
                <findspec mode="all" disjoint="0">
                  <conditions>
                    <condition test="wildcard" flags="0">
                      <category><name internal="LcOaNode">Item</name></category>
                      <property><name internal="LcOaNodeSourceFile">Source File</name></property>
                      <value><data type="wstring">*ARC_*</data></value>
                    </condition>
                    <condition test="contains" flags="0">
                      <category><name internal="LcOaNode">Item</name></category>
                      <property><name internal="LcOaSceneBaseClassUserName">Type</name></property>
                      <value><data type="wstring">Стен</data></value>
                    </condition>
                    <condition test="wildcard" flags="64">
                      <category><name internal="LcOaNode">Item</name></category>
                      <property><name internal="LcOaNodeSourceFile">Source File</name></property>
                      <value><data type="wstring">*ARC_*</data></value>
                    </condition>
                    <condition test="contains" flags="0">
                      <category><name internal="LcOaNode">Item</name></category>
                      <property><name internal="LcOaSceneBaseClassUserName">Type</name></property>
                      <value><data type="wstring">Wall</data></value>
                    </condition>
                  </conditions>
                  <locator>/</locator>
                </findspec>
              </selectionset>
            </viewfolder>
            <selectionset name="Picked by hand" />
            <selectionset name="Level">
              <findspec mode="all" disjoint="1">
                <conditions>
                  <condition test="equals" flags="26">
                    <category><name internal="LcRevitData">Element</name></category>
                    <property><name internal="revit_Base Level">Base Level</name></property>
                    <value><data type="name"><name internal="LcRevitElement">Level "FIRST FLOOR", #57775</name></data></value>
                  </condition>
                </conditions>
                <locator>/</locator>
              </findspec>
            </selectionset>
          </selectionsets>
        </exchange>
        """;

    private const string _clashTests = """
        <?xml version="1.0" encoding="UTF-8" ?>
        <exchange units="m" filename="" filepath="">
          <batchtest name="Batch" internal_name="Batch" units="m">
            <clashtests>
              <clashtest name="ARC_Walls-vs-HVC_Pipes>Dn50" test_type="hard" tolerance="0.05">
                <left><clashselection><locator>lcop_selection_set_tree/ARC/ARC_Walls</locator></clashselection></left>
                <right><clashselection><locator>lcop_selection_set_tree/HVC/HVC_Pipes&gt;Dn50</locator></clashselection></right>
              </clashtest>
              <clashtest name="Self" test_type="hard" tolerance="0">
                <right><clashselection><locator>lcop_selection_set_tree/ARC/ARC_Walls</locator></clashselection></right>
              </clashtest>
              <clashtest name="Tree" test_type="hard" tolerance="0">
                <left><clashselection><locator>lcop_selection_tree/Model.nwc</locator></clashselection></left>
                <right><clashselection><locator>lcop_selection_set_tree/ARC/ARC_Walls</locator></clashselection></right>
              </clashtest>
            </clashtests>
          </batchtest>
        </exchange>
        """;

    [Fact]
    public void Or_flag_starts_a_new_group() {
        var exchange = NavisExchange.Parse(XDocument.Parse(_searchSets));
        var walls = exchange.SearchSets.Single(item => item.Name == "ARC_Walls");

        Assert.Equal(2, walls.Groups.Count);
        Assert.Equal(["*ARC_*", "Стен"], walls.Groups[0].Select(item => item.Value));
        Assert.Equal(["*ARC_*", "Wall"], walls.Groups[1].Select(item => item.Value));
    }

    [Fact]
    public void Set_path_includes_its_folders() {
        var exchange = NavisExchange.Parse(XDocument.Parse(_searchSets));

        Assert.Contains(exchange.SearchSets, item => item.Path == "ARC/ARC_Walls");
        Assert.Contains(exchange.SearchSets, item => item.Path == "Level");
    }

    [Fact]
    public void Set_without_findspec_is_an_explicit_selection() {
        var exchange = NavisExchange.Parse(XDocument.Parse(_searchSets));

        Assert.True(exchange.SearchSets.Single(item => item.Name == "Picked by hand").IsExplicitSelection);
    }

    [Fact]
    public void Name_value_keeps_the_visible_name() {
        var exchange = NavisExchange.Parse(XDocument.Parse(_searchSets));
        var condition = exchange.SearchSets.Single(item => item.Name == "Level").Groups.Single().Single();

        Assert.Equal("name", condition.DataType);
        Assert.Equal("Level \"FIRST FLOOR\", #57775", condition.Value);
        Assert.Equal("revit_Base Level", condition.PropertyInternal);
    }

    [Fact]
    public void Units_and_tolerance_are_read_as_written() {
        var exchange = NavisExchange.Parse(XDocument.Parse(_clashTests));

        Assert.Equal("m", exchange.Units);
        Assert.Equal(0.05, exchange.ClashTests.First().Tolerance, 6);
    }

    [Fact]
    public void Test_sides_are_set_paths() {
        var test = NavisExchange.Parse(XDocument.Parse(_clashTests)).ClashTests.First();

        Assert.Equal("ARC/ARC_Walls", test.Left);
        Assert.Equal("HVC/HVC_Pipes>Dn50", test.Right);
        Assert.Null(test.UnsupportedLocator);
    }

    [Fact]
    public void Test_with_one_side_has_the_other_empty() {
        var test = NavisExchange.Parse(XDocument.Parse(_clashTests)).ClashTests.Single(item => item.Name == "Self");

        Assert.Null(test.Left);
        Assert.Equal("ARC/ARC_Walls", test.Right);
    }

    [Fact]
    public void Model_tree_locator_is_reported_as_unsupported() {
        var test = NavisExchange.Parse(XDocument.Parse(_clashTests)).ClashTests.Single(item => item.Name == "Tree");

        Assert.Equal("lcop_selection_tree/Model.nwc", test.UnsupportedLocator);
    }

    [Fact]
    public void Non_exchange_document_is_not_recognised() {
        Assert.False(NavisExchange.IsNavisworksExchange(XDocument.Parse("<config />")));
    }
}
