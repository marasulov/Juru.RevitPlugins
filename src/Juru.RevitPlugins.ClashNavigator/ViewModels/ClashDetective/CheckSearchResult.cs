namespace Juru.RevitPlugins.ClashNavigator.ViewModels.ClashDetective;
/// <summary>
/// Итог одного прогона проверки: что нашлось и чем это отличается от прошлого раза.
/// </summary>
internal sealed class CheckSearchResult {
    public CheckSearchResult(string checkName, int total, int added, int gone, bool hadPreviousRun) {
        CheckName = checkName;
        Total = total;
        Added = added;
        Gone = gone;
        HadPreviousRun = hadPreviousRun;
    }

    public string CheckName { get; }

    /// <summary>
    /// Сколько коллизий нашёл этот прогон.
    /// </summary>
    public int Total { get; }

    /// <summary>
    /// Сколько из них не было в прошлом прогоне.
    /// </summary>
    public int Added { get; }

    /// <summary>
    /// Сколько коллизий прошлого прогона в этом не встретилось.
    /// </summary>
    public int Gone { get; }

    /// <summary>
    /// Было ли с чем сравнивать. При первом прогоне все коллизии формально новые, и писать об
    /// этом отдельной цифрой незачем.
    /// </summary>
    public bool HadPreviousRun { get; }
}
