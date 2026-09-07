using Juru.RevitPlugins.Core.FileNaming;

using Xunit;

namespace Juru.RevitPlugins.Tests;
/// <summary>
/// Сопоставление имени объекта из имени файла с именем в базе сервера.
/// </summary>
/// <remarks>
/// В именах файлов пробел пишут подчёркиванием. Пока это не учитывалось, отправка заданий из
/// BASH2-Dormitory_building-HVC.rvt заводила на сервере второй объект-двойник рядом с настоящим:
/// сервер отвечал "принято", а заданий не видел ни фронт, ни плагин АР/КР - см. Changelog 0.0.20.
/// </remarks>
public class ServerNamesTests {
    [Fact]
    public void Match_TreatsUnderscoreAsSpace() {
        Assert.True(ServerNames.Match("Dormitory building", "Dormitory_building"));
    }

    [Fact]
    public void Match_IgnoresCase() {
        Assert.True(ServerNames.Match("Dormitory Building", "dormitory_building"));
    }

    [Fact]
    public void Match_IgnoresRepeatedAndSurroundingSpaces() {
        Assert.True(ServerNames.Match("  Dormitory   building ", "Dormitory_building"));
    }

    [Fact]
    public void Match_SeparatesDifferentObjects() {
        Assert.False(ServerNames.Match("Dormitory building", "Dormitory_building_2"));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", null)]
    public void Match_TreatsEmptyNamesAsEqual(string serverName, string fileName) {
        Assert.True(ServerNames.Match(serverName, fileName));
    }

    [Fact]
    public void Match_DoesNotMatchEmptyNameWithRealOne() {
        Assert.False(ServerNames.Match(null, "Dormitory_building"));
    }

    [Theory]
    [InlineData("Dormitory_building", "Dormitory building")]
    [InlineData("  Dormitory   building  ", "Dormitory building")]
    [InlineData(null, "")]
    public void Normalize_BringsNameToComparableForm(string name, string expected) {
        Assert.Equal(expected, ServerNames.Normalize(name));
    }
}
