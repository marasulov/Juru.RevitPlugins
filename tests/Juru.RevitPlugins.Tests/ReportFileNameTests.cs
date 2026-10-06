using System.IO;
using System.Linq;

using Juru.RevitPlugins.ClashNavigator.Models.Clashes;

using Xunit;

namespace Juru.RevitPlugins.Tests;

/// <summary>
/// Имя файла отчёта из имени проверки.
/// </summary>
public class ReportFileNameTests {
    [Fact]
    public void Ordinary_name_is_unchanged() {
        Assert.Equal("ARC_ACWA_ARC стены × ELE лотки", ReportFileName.From("ARC_ACWA_ARC стены × ELE лотки"));
    }

    [Fact]
    public void Greater_and_less_stay_distinct() {
        string greater = ReportFileName.From("HVC_Pipes>Dn50");
        string less = ReportFileName.From("HVC_Pipes<Dn50");

        Assert.NotEqual(greater, less);
    }

    [Theory]
    [InlineData("a<b>c:d\"e/f\\g|h?i*j")]
    [InlineData("tab\there")]
    public void Result_is_a_valid_file_name(string name) {
        string fileName = ReportFileName.From(name);

        Assert.DoesNotContain(fileName, ch => Path.GetInvalidFileNameChars().Contains(ch));
    }

    [Fact]
    public void Conversion_is_idempotent() {
        string once = ReportFileName.From("HVC_Pipes>Dn50");

        Assert.Equal(once, ReportFileName.From(once));
    }
}
