using Juru.RevitPlugins.Core.FileNaming;

using Xunit;

namespace Juru.RevitPlugins.Tests;
/// <summary>
/// Разбор имени файла по стандарту наименования организации:
/// Проект-Название_объекта-Дисциплина-vВерсия.rvt
/// </summary>
/// <remarks>
/// Эти правила дважды ломались в проде, и оба раза молча: разбор возвращал false, а вызывающий
/// код считал, что подходящих связей просто нет. Случаи из тех разборов - ниже.
/// </remarks>
public class RevitFileNameInfoTests {
    [Fact]
    public void TryParse_ReadsAllPartsOfStandardName() {
        Assert.True(RevitFileNameInfo.TryParse("BASH2-Dormitory_building-HVC-v24.rvt", out var info));

        Assert.Equal("BASH2", info.ProjectCode);
        Assert.Equal("Dormitory_building", info.ObjectName);
        Assert.Equal(Discipline.HVC, info.Discipline);
        Assert.Equal(24, info.RevitVersion);
    }

    [Fact]
    public void TryParse_AcceptsFullPath() {
        Assert.True(RevitFileNameInfo.TryParse(
            @"C:\Projects\BASH2\BASH2-Dormitory_building-ARC-v24.rvt", out var info));

        Assert.Equal(Discipline.ARC, info.Discipline);
    }

    /// <summary>
    /// Версия ревита в имени есть не у всех файлов, и её отсутствие не должно ронять разбор:
    /// её всё равно никто не спрашивает.
    /// </summary>
    [Fact]
    public void TryParse_AcceptsNameWithoutRevitVersion() {
        Assert.True(RevitFileNameInfo.TryParse("BASH2-Dormitory_building-STR.rvt", out var info));

        Assert.Equal(Discipline.STR, info.Discipline);
        Assert.Equal(0, info.RevitVersion);
    }

    /// <summary>
    /// Связи подгружают копиями с постфиксом. Пока такие имена не разбирались, выдача заданий на
    /// отверстия не находила ни одной связи с конструкциями - см. Changelog 0.0.19.
    /// </summary>
    [Theory]
    [InlineData("BASH2-Dormitory_building-ARC_shared.rvt")]
    [InlineData("BASH2-Dormitory_building-ARC-v24_detached.rvt")]
    [InlineData("BASH2-Dormitory_building-ARC (копия).rvt")]
    public void TryParse_AcceptsSuffixAfterDiscipline(string fileName) {
        Assert.True(RevitFileNameInfo.TryParse(fileName, out var info));

        Assert.Equal(Discipline.ARC, info.Discipline);
        Assert.Equal("Dormitory_building", info.ObjectName);
    }

    /// <summary>
    /// Хвост отделён разделителем как раз затем, чтобы ARCHIVE не разобрался как ARC с хвостом.
    /// </summary>
    [Fact]
    public void TryParse_DoesNotTakeLongerWordForDiscipline() {
        Assert.False(RevitFileNameInfo.TryParse("BASH2-Dormitory_building-ARCHIVE.rvt", out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Просто имя без стандарта.rvt")]
    [InlineData("BASH2-Dormitory_building-XXX-v24.rvt")]
    public void TryParse_RejectsNameOutsideStandard(string fileName) {
        Assert.False(RevitFileNameInfo.TryParse(fileName, out var info));
        Assert.Null(info);
    }

    [Fact]
    public void TryParse_IgnoresDisciplineCase() {
        Assert.True(RevitFileNameInfo.TryParse("BASH2-Dormitory_building-hvc-v24.rvt", out var info));

        Assert.Equal(Discipline.HVC, info.Discipline);
    }

    /// <summary>
    /// Длина кода проекта не проверяется: на сервере есть и BASH2, и O&amp;M, и DEMO.
    /// </summary>
    [Theory]
    [InlineData("O&M-Dormitory_building-ARC.rvt", "O&M")]
    [InlineData("DEMO-Dormitory_building-ARC.rvt", "DEMO")]
    public void TryParse_AcceptsProjectCodeOfAnyLength(string fileName, string expected) {
        Assert.True(RevitFileNameInfo.TryParse(fileName, out var info));

        Assert.Equal(expected, info.ProjectCode);
    }
}
