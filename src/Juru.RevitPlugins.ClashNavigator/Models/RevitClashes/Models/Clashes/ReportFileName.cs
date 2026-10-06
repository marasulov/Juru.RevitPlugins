using System.IO;
using System.Linq;
using System.Text;

namespace Juru.RevitPlugins.ClashNavigator.Models.Clashes;
/// <summary>
/// Имя файла отчёта, построенное из имени проверки.
/// </summary>
/// <remarks>
/// Имя проверки свободное: тесты из Navisworks называются, например, <c>ARC_Walls-vs-HVC_Pipes&gt;Dn50</c>,
/// а символы <c>&lt;&gt;:"/\|?*</c> в имени файла запрещены. Подчёркивание вместо них не годится:
/// <c>Pipes&gt;Dn50</c> и <c>Pipes&lt;Dn50</c> дали бы одно имя, и две проверки писали бы в один
/// отчёт. Поэтому они заменяются похожими широкими символами Юникода - имя остаётся различимым
/// и читаемым. Имена без таких символов не меняются, так что старые отчёты находятся как раньше.
/// </remarks>
internal static class ReportFileName {
    private const string _forbidden = "<>:\"/\\|?*";
    private const string _replacements = "＜＞：＂／＼｜？＊";

    public static string From(string reportName) {
        if(string.IsNullOrEmpty(reportName)) {
            return reportName;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(reportName.Length);
        foreach(char ch in reportName) {
            int index = _forbidden.IndexOf(ch);
            if(index >= 0) {
                builder.Append(_replacements[index]);
            } else if(invalid.Contains(ch)) {
                builder.Append('_');
            } else {
                builder.Append(ch);
            }
        }
        return builder.ToString();
    }
}
