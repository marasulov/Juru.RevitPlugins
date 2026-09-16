using Juru.RevitPlugins.Schedules.Services;

using Xunit;

namespace Juru.RevitPlugins.Tests;

/// <summary>
///     Разбор текста ячеек спецификации: что попадает в словарь ключом и в каком виде.
/// </summary>
public class RussianTextTests
{
    [Theory]
    // Ровно так плагин пишет двуязычное значение: «исходник — разделитель — перевод».
    [InlineData("Кабинет / Office", "Кабинет", "Office")]
    [InlineData("Номер листа | Sheet number", "Номер листа", "Sheet number")]
    [InlineData("Наименование — Name", "Наименование", "Name")]
    [InlineData("Примечание ; Note", "Примечание", "Note")]
    [InlineData("Обозначение - Designation", "Обозначение", "Designation")]
    [InlineData("Ведомость отделки\nFinish schedule", "Ведомость отделки", "Finish schedule")]
    // Обратный порядок половин: так объекты названы в самой модели, а не записаны плагином.
    // Исходником всё равно считается русская половина, переводом - та, что без кириллицы.
    [InlineData("Heating / Отопление", "Отопление", "Heating")]
    [InlineData("Issued for Construction / Выпущено для строительства",
        "Выпущено для строительства", "Issued for Construction")]
    public void TrySplitBilingual_SplitsWrittenValue(string written, string source, string translation)
    {
        Assert.True(RussianText.TrySplitBilingual(written, out var actualSource, out var actualTranslation));
        Assert.Equal(source, actualSource);
        Assert.Equal(translation, actualTranslation);
    }

    [Theory]
    // Марка изделия: дефис без пробелов вокруг разделителем не считается, иначе «ДВ-2» распалось бы.
    [InlineData("ДВ-2")]
    [InlineData("Кабинет-2")]
    [InlineData("ФЭ-П-1")]
    // Одноязычные строки делить не на что.
    [InlineData("Ведомость отделки помещений")]
    [InlineData("Office")]
    // Обе половины русские: это не перевод, а перечисление.
    [InlineData("Кабинет / Кладовая")]
    // Обе половины без кириллицы - тоже не пара.
    [InlineData("Heating / Ventilation")]
    // Справа нет букв: это размер, а не перевод.
    [InlineData("Проём / 100х200")]
    public void TrySplitBilingual_LeavesSingleLanguageTextAlone(string text)
    {
        Assert.False(RussianText.TrySplitBilingual(text, out _, out _));
    }

    [Theory]
    // Перенос слова в узкой колонке: дефис и пробелы.
    [InlineData("Кат. поме-  ще-  ния", "Кат. помещения")]
    [InlineData("Номер помеще- ния", "Номер помещения")]
    // То же, но перенесено настоящим переводом строки.
    [InlineData("Номер помеще-\nния", "Номер помещения")]
    [InlineData("Площадь по-\r\nмещения", "Площадь помещения")]
    // Дефис внутри слова переносом не является и остаётся на месте.
    [InlineData("Юго-западный фасад", "Юго-западный фасад")]
    [InlineData("ДВ-2", "ДВ-2")]
    // Перенос строки без дефиса - настоящий перенос, он сохраняется.
    [InlineData("Ведомость\nотделки", "Ведомость\nотделки")]
    public void Normalize_JoinsHyphenatedWords(string cell, string expected)
    {
        Assert.Equal(expected, RussianText.Normalize(cell));
    }

    [Theory]
    [InlineData("  Наименование  ", "Наименование")]
    [InlineData("Масса  ед.,   кг", "Масса ед., кг")]
    public void Normalize_CollapsesSpacing(string cell, string expected)
    {
        Assert.Equal(expected, RussianText.Normalize(cell));
    }

    [Theory]
    // Многострочные половины: текст ячейки переносят руками, и разделитель стоит посреди переносов.
    // Случаи взяты из словаря реальной модели.
    [InlineData("Heat\nconsumption, W / Расход\nхолода Вт",
        "Расход\nхолода Вт", "Heat\nconsumption, W")]
    [InlineData("1 unit, kg /\nМасса\n1 ед., кг",
        "Масса\n1 ед., кг", "1 unit, kg")]
    [InlineData("System\nidenti-\nfication /\nОбозна-\nчение\nсистемы",
        "Обозна-\nчение\nсистемы", "System\nidenti-\nfication")]
    // Разделитель один на всей строке.
    [InlineData("Name of Serviced Premises\n(process equipment)\n/\nНаименование обслуживаемого\nпомещения",
        "Наименование обслуживаемого\nпомещения", "Name of Serviced Premises\n(process equipment)")]
    // Пробел только справа от разделителя.
    [InlineData("Gas storage room FM-200/ Помещение хранения",
        "Помещение хранения", "Gas storage room FM-200")]
    public void TrySplitBilingual_SplitsMultilineHalves(string written, string source, string translation)
    {
        Assert.True(RussianText.TrySplitBilingual(written, out var actualSource, out var actualTranslation));
        Assert.Equal(source, actualSource);
        Assert.Equal(translation, actualTranslation);
    }

    [Theory]
    // Дробь внутри единиц измерения: пробела рядом нет, делить нечего.
    [InlineData("n (об/мин)")]
    [InlineData("Расход, м³/ч")]
    // Перенос строки сам по себе пару не образует: обе половины русские.
    [InlineData("Расход\nтеплоты, Вт")]
    public void TrySplitBilingual_LeavesUnitsAndWrappedTextAlone(string text)
    {
        Assert.False(RussianText.TrySplitBilingual(text, out _, out _));
    }

    [Theory]
    // Половина пары, у которой вторая половина пуста: разделитель остался висеть на краю.
    [InlineData("/ Ко-ч.", "Ко-ч.")]
    [InlineData("/ Нач.", "Нач.")]
    [InlineData("/ от", "от")]
    [InlineData("1 unit, kg /", "1 unit, kg")]
    [InlineData("| Обозначение |", "Обозначение")]
    // Дефис по краю не разделитель: он часть значения.
    [InlineData("-10 мм", "-10 мм")]
    [InlineData("Наименование", "Наименование")]
    public void TrimSeparators_DropsDanglingSeparators(string text, string expected)
    {
        Assert.Equal(expected, RussianText.TrimSeparators(text));
    }

    [Theory]
    // Явного разделителя нет, половины разнесены только ручными переносами. Разрыв обязан пройти
    // по границе языка, а не по первому переносу: случай из словаря реальной модели.
    [InlineData("Building (structure), \npremise name \nНаименование здания \n(сооружения), помещения",
        "Наименование здания \n(сооружения), помещения", "Building (structure), \npremise name")]
    [InlineData("Cold consumption, W \nРасход холода, Вт", "Расход холода, Вт", "Cold consumption, W")]
    // Русский слева, английский справа - разрыв после последней кириллицы.
    [InlineData("Расход холода, Вт\nCold consumption, W", "Расход холода, Вт", "Cold consumption, W")]
    public void TrySplitBilingual_SplitsAtLanguageBoundary(string written, string source, string translation)
    {
        Assert.True(RussianText.TrySplitBilingual(written, out var actualSource, out var actualTranslation));
        Assert.Equal(source, actualSource);
        Assert.Equal(translation, actualTranslation);
    }

    [Theory]
    // Пару переводили целиком, и в переводе оказались обе половины. Берётся та, что стоит на месте
    // русского исходника: только она переведена с него.
    [InlineData("Design\ndia-gram / Схема исполнения", "Design\ndia-gram / Diagram of Implementation",
        "Diagram of Implementation")]
    [InlineData("Cold consumption, W \nРасход холода, Вт", "Cold consumption, W \nCold consumption, W",
        "Cold consumption, W")]
    [InlineData("Наименование систем / Name of systems", "Name of systems / System names", "Name of systems")]
    public void TryTakeSameHalf_KeepsTheHalfTranslatedFromTheSource(string key, string doubled, string expected)
    {
        Assert.True(RussianText.TrySplitBilingual(key, out _, out _, out var shape));
        Assert.True(RussianText.TryTakeSameHalf(doubled, shape, out var half));
        Assert.Equal(expected, half);
    }
}
