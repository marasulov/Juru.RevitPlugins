namespace Juru.RevitPlugins.Schedules.Services;

/// <summary>
///     Отличает строки, подлежащие переводу, от кодов, номеров и уже английского текста.
/// </summary>
public static class RussianText
{
    public static bool ContainsCyrillic(string? text)
    {
        if (text is null)
        {
            return false;
        }

        foreach (var symbol in text)
        {
            if (symbol is >= 'Ѐ' and <= 'ӿ')
            {
                return true;
            }
        }

        return false;
    }
}
