namespace Juru.RevitPlugins.Schedules.Models;

/// <summary>
///     Откуда взялся перевод строки. Машинный перевод помечается, чтобы его было видно при проверке.
/// </summary>
public enum TranslationSource
{
    /// <summary>Перевода нет.</summary>
    None,

    /// <summary>Выведен из самой строки: английская половина, обозначение стандарта, марка изделия.</summary>
    Derived,

    /// <summary>Получен от службы перевода и не проверен человеком.</summary>
    Machine,

    /// <summary>Введён или исправлен человеком.</summary>
    Human
}
