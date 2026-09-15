using System.Collections.Generic;

namespace Juru.RevitPlugins.Schedules.Services.Categories;

/// <summary>
///     Категории модели, чей текст плагин умеет переводить.
/// </summary>
/// <remarks>
///     Порядок здесь — порядок вкладок в окне. Собственного текста спецификаций тут нет: сами
///     спецификации выбирают на своей вкладке, а способ перевода задаёт галочка «Перерисовать
///     в линии», поэтому <see cref="ScheduleTexts" /> вызывается оттуда, а не как вкладка.
///     Ключевые примечания лежат во внешнем файле, а не в элементах модели, поэтому их перевод
///     записывается перезаписью файла, а не через <c>ElementTextWriter</c> — это делает
///     <c>EnglishSetBuilder</c>.
/// </remarks>
public static class CategoryRegistry
{
    public static IReadOnlyList<ITranslatableCategory> All { get; } =
    [
        new Rooms(),
        new Areas(),
        new Sheets(),
        new Views(),
        new TextNotes(),
        new Levels(),
        new Materials(),
        new Revisions(),
        new Dimensions(),
        new FamilyTypes(),
        new Keynotes(),
        new GlobalParameters()
    ];
}
