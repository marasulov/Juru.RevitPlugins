using System.Linq;

using Juru.RevitPlugins.SystemViews.Services;

using Xunit;

namespace Juru.RevitPlugins.Tests;

/// <summary>
/// Разбор значения параметра системы и условия, которые плагин «Виды по системам» пишет в фильтр вида.
/// </summary>
/// <remarks>
/// Ошибка здесь тихая: схема В1 показала бы и В10, или потеряла бы оборудование «П1,В1».
/// </remarks>
public class SystemNamesTests {
    [Theory]
    [InlineData("В1", new[] { "В1" })]
    [InlineData("П1,В1", new[] { "П1", "В1" })]
    [InlineData("П1, В1; Т1", new[] { "П1", "В1", "Т1" })]
    [InlineData(" В1 ,, в1 ", new[] { "В1" })]
    [InlineData("", new string[0])]
    [InlineData(null, new string[0])]
    public void Split_ReturnsEverySystemOnce(string? value, string[] expected) {
        Assert.Equal(expected, SystemNames.Split(value));
    }

    [Theory]
    [InlineData("В1", "В1", true)]
    [InlineData("в1", "В1", true)]
    [InlineData("В10", "В1", false)]
    [InlineData("В1.1", "В1", false)]
    [InlineData("ПВ1", "В1", false)]
    [InlineData("В1,В2", "В1", true)]
    [InlineData("В1,В2", "В2", true)]
    [InlineData("В1, В2", "В2", true)]
    [InlineData("В10, В2", "В1", false)]
    [InlineData("П1, В1, Т1", "В1", true)]
    [InlineData("П1;В1;Т1", "В1", true)]
    [InlineData("П1; В11; Т1", "В1", false)]
    [InlineData("", "В1", false)]
    public void IsMatch_TakesWholeSystemNamesOnly(string value, string system, bool expected) {
        Assert.Equal(expected, SystemNames.IsMatch(value, system));
    }

    /// <summary>
    /// Число элементов в окне считается по <see cref="SystemNames.IsMatch" />, а список систем - по
    /// <see cref="SystemNames.Split" />: для обычных значений они обязаны соглашаться, иначе в окне
    /// появится система, у которой на схеме ничего не останется.
    /// </summary>
    [Theory]
    [InlineData("В1")]
    [InlineData("П1,В1")]
    [InlineData("П1, В1, Т1")]
    [InlineData("К1;К2;К3")]
    public void IsMatch_AgreesWithSplit(string value) {
        Assert.All(SystemNames.Split(value), system => Assert.True(SystemNames.IsMatch(value, system)));
    }

    [Fact]
    public void MatchPatterns_HaveNoBareContains() {
        Assert.DoesNotContain(
            SystemNames.MatchPatterns("В1"),
            pattern => pattern.Kind == MatchKind.Contains && pattern.Text == "В1");
    }

    [Fact]
    public void Names_ReplaceCharactersRevitRejects() {
        Assert.Equal("Схема В1_2", SystemNames.ViewName("Схема ", "В1:2"));
        Assert.Equal("Схема - кроме К_1_", SystemNames.FilterName("К[1]"));
        Assert.StartsWith(SystemNames.FilterPrefix, SystemNames.FilterName("В1"));
        Assert.Equal("U_HVAC_O_CX_Вентиляция - В 1", SystemNames.TemplateName("U_HVAC_O_CX_Вентиляция", "В 1"));
    }

    [Fact]
    public void Split_KeepsOrderOfFirstAppearance() {
        Assert.Equal(new[] { "Т1", "П1" }, SystemNames.Split("Т1, П1, т1").ToArray());
    }
}
