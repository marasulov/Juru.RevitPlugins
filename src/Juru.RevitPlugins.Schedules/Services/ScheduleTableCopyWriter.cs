using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Schedules.Models;

using RevitException = Autodesk.Revit.Exceptions.ApplicationException;

namespace Juru.RevitPlugins.Schedules.Services;

/// <summary>
///     Собирает английскую копию спецификации новой спецификацией: тот же текст, те же строки и колонки,
///     только переведённые.
/// </summary>
/// <remarks>
///     <para>
///         Тело обычной спецификации через API не переопределяется, а секция шапки - переопределяется
///         свободно: она не привязана к элементам модели. На этом в проектах и держатся таблицы, набранные
///         руками, - список сокращений, общие указания: у них пустое тело, а весь текст лежит в шапке.
///         Копия делается такой же: заводится спецификация по категории, в которой нет ни одного элемента
///         (тело тогда пусто), а прочитанная таблица целиком выкладывается в её шапку.
///     </para>
///     <para>
///         Отличие от копии линиями: копия остаётся спецификацией - её видно в диспетчере среди
///         спецификаций, она кладётся на лист как спецификация и печатается как таблица, а не как набор
///         отрезков и надписей. Общее у них одно: копия статична и с моделью больше не связана.
///     </para>
/// </remarks>
public sealed class ScheduleTableCopyWriter(Document document)
{
    /// <summary>
    ///     Разные секции делят таблицу на колонки по-своему: у шапки одна колонка на всю ширину, у тела -
    ///     свои. Общая сетка строится по всем границам сразу, и колонка секции ложится в несколько колонок
    ///     сетки. Границы сравниваются с этим допуском - иначе одна и та же граница, посчитанная по разным
    ///     секциям, даст лишнюю колонку шириной в микрон.
    /// </summary>
    private const double EdgeTolerance = 1e-4;

    /// <summary>Как <see cref="ScheduleTableReader" /> называет секцию названия таблицы.</summary>
    private const string TitleSectionName = "Header";

    /// <summary>Категории, по которым заводится копия: годится первая, в которой нет ни одного элемента.</summary>
    /// <remarks>
    ///     Элемент категории попал бы в тело копии строкой, которой в оригинале нет. Ни одной пустой не
    ///     нашлось - берётся первая: лишние строки в теле хуже, чем отсутствие копии, но не настолько,
    ///     чтобы отказываться её делать.
    /// </remarks>
    private static readonly BuiltInCategory[] EmptyCategoryCandidates =
    [
        BuiltInCategory.OST_Planting,
        BuiltInCategory.OST_Site,
        BuiltInCategory.OST_Mass,
        BuiltInCategory.OST_Roads,
        BuiltInCategory.OST_Parking
    ];

    private int skippedCellCount;

    /// <summary>
    ///     Создаёт спецификацию-копию и заполняет её шапку переведённым текстом. Метод обязан выполняться
    ///     внутри открытой транзакции.
    /// </summary>
    /// <param name="copyName">Имя копии: у спецификаций оно уникально, поэтому несёт пометку копии.</param>
    /// <param name="originalName">
    ///     Имя оригинала. Идёт в строку названия, если та пуста: у обычной спецификации название - это её
    ///     имя, а не переопределённый текст ячейки, и в снимке такая ячейка пуста. Без этого копия
    ///     печаталась бы со своим служебным именем вместо переведённого названия таблицы.
    /// </param>
    public ScheduleTableCopyResult Write(
        ScheduleTableSnapshot snapshot,
        Glossary glossary,
        string copyName,
        string originalName)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        if (glossary is null)
        {
            throw new ArgumentNullException(nameof(glossary));
        }

        skippedCellCount = 0;

        var layout = Layout.Build(snapshot);
        var copy = Create(copyName);
        var header = copy.GetTableData().GetSectionData(SectionType.Header);

        Resize(header, layout);
        document.Regenerate();

        Fill(copy, layout, glossary);
        FillTitle(copy, glossary, originalName);
        document.Regenerate();

        return new ScheduleTableCopyResult
        {
            ViewId = copy.Id,
            ViewName = copy.Name,
            RowCount = layout.RowHeights.Count,
            ColumnCount = layout.ColumnWidths.Count,
            WidthInFeet = layout.ColumnWidths.Sum(),
            SkippedCellCount = skippedCellCount
        };
    }

    /// <summary>
    ///     Заводит пустую спецификацию под копию. Имя занято - Ревит отклоняет его исключением, и к имени
    ///     дописывается номер: прогон не должен срываться из-за того, что прежнюю копию не удалось удалить.
    /// </summary>
    private ViewSchedule Create(string copyName)
    {
        var view = ViewSchedule.CreateSchedule(document, PickEmptyCategory());
        var definition = view.Definition;

        // Ни одного поля: тогда у тела нет ни одной колонки, и рисовать ему нечего. С полем у тела
        // остаётся пустая строка со своими границами - полоса над таблицей, которую ни убрать, ни
        // сделать ниже нельзя: высота строк и стиль ячеек тела обычной спецификации не задаются.
        definition.ShowHeaders = false;
        definition.ShowTitle = true;

        for (var attempt = 0; attempt < 100; attempt++)
        {
            try
            {
                view.Name = attempt is 0 ? copyName : $"{copyName} {attempt + 1}";

                return view;
            }
            catch (RevitException)
            {
                // Имя занято: пробуем следующее.
            }
        }

        return view;
    }

    private ElementId PickEmptyCategory()
    {
        foreach (var candidate in EmptyCategoryCandidates)
        {
            if (Category.GetCategory(document, candidate) is not { } category)
            {
                continue;
            }

            var any = new FilteredElementCollector(document)
                .OfCategoryId(category.Id)
                .WhereElementIsNotElementType()
                .FirstElement();

            if (any is null)
            {
                return category.Id;
            }
        }

        return Category.GetCategory(document, EmptyCategoryCandidates[0])?.Id
               ?? new ElementId(EmptyCategoryCandidates[0]);
    }

    /// <summary>
    ///     Доводит шапку до размеров сетки и раздаёт колонкам и строкам размеры оригинала. Отказ в размере
    ///     не срывает копию: Ревит держит свои пределы на высоту строки, и таблица с чуть иной строкой
    ///     полезнее, чем её отсутствие.
    /// </summary>
    private static void Resize(TableSectionData header, Layout layout)
    {
        while (header.NumberOfColumns < layout.ColumnWidths.Count)
        {
            header.InsertColumn(header.NumberOfColumns);
        }

        while (header.NumberOfRows < layout.RowHeights.Count)
        {
            header.InsertRow(header.NumberOfRows);
        }

        for (var column = 0; column < layout.ColumnWidths.Count; column++)
        {
            TrySet(() => header.SetColumnWidth(column, layout.ColumnWidths[column]));
        }

        for (var row = 0; row < layout.RowHeights.Count; row++)
        {
            TrySet(() => header.SetRowHeight(row, layout.RowHeights[row]));
        }
    }

    private void Fill(ViewSchedule copy, Layout layout, Glossary glossary)
    {
        foreach (var cell in layout.Cells)
        {
            var header = copy.GetTableData().GetSectionData(SectionType.Header);

            if (!header.IsValidRowNumber(cell.Row) || !header.IsValidColumnNumber(cell.Column))
            {
                skippedCellCount++;
                continue;
            }

            if (cell.Row != cell.LastRow || cell.Column != cell.LastColumn)
            {
                TrySet(() => header.MergeCells(Region(cell)));
            }

            try
            {
                header.SetCellText(cell.Row, cell.Column, glossary.Translate(cell.Source.Text));
            }
            catch (RevitException)
            {
                skippedCellCount++;
                continue;
            }

            ApplyStyle(copy.GetTableData().GetSectionData(SectionType.Header), cell);
        }
    }

    /// <summary>
    ///     Объединяемая область ячейки.
    /// </summary>
    /// <remarks>
    ///     Границы задаются свойствами, а не конструктором: у <c>TableMergedCell</c> он принимает их в
    ///     порядке «верх, лево, низ, право», и привычное «верх, низ, лево, право» собирает область
    ///     наизнанку. Ревит отклоняет её как выходящую за пределы, и строка остаётся необъединённой -
    ///     заголовок раздела, растянутый в оригинале на всю ширину, сжимался в первую колонку.
    /// </remarks>
    private static TableMergedCell Region(LayoutCell cell)
    {
        return new TableMergedCell
        {
            Top = cell.Row,
            Bottom = cell.LastRow,
            Left = cell.Column,
            Right = cell.LastColumn
        };
    }

    /// <summary>
    ///     Дописывает название таблицы, если строка названия осталась пустой.
    /// </summary>
    private static void FillTitle(ViewSchedule copy, Glossary glossary, string originalName)
    {
        var header = copy.GetTableData().GetSectionData(SectionType.Header);

        if (header.NumberOfRows is 0 || header.NumberOfColumns is 0)
        {
            return;
        }

        if ((copy.GetCellText(SectionType.Header, 0, 0) ?? string.Empty).Trim().Length > 0
            && (header.GetCellText(0, 0) ?? string.Empty).Trim().Length > 0)
        {
            return;
        }

        TrySet(() => header.SetCellText(0, 0, glossary.Translate(ScheduleCopyName.TitleOf(originalName))));
    }

    /// <summary>
    ///     Переносит начертание оригинала. Стиль пишется поверх прочитанного у самой ячейки, а не собирается
    ///     с нуля: у копии остаются шрифт и границы, которые Ревит задал ей по шаблону.
    /// </summary>
    private static void ApplyStyle(TableSectionData header, LayoutCell cell)
    {
        TrySet(() =>
        {
            var style = header.GetTableCellStyle(cell.Row, cell.Column);

            style.IsFontBold = cell.Source.IsBold;
            style.IsFontItalic = cell.Source.IsItalic;
            style.FontHorizontalAlignment = Horizontal(cell.Source.Alignment);
            style.TextSize = cell.Source.TextSize;

            // Из границ переносится только одно: какие боковые линии в оригинале убраны. Колонка-распорка
            // в 3 мм слита с соседней именно так - у неё нет боковой линии, и в оригинале её не видно, а
            // копия без этого обводила её полной сеткой и показывала пустую колонку.
            var hideLeft = cell.Source.Borders.Left <= 0;
            var hideRight = cell.Source.Borders.Right <= 0;

            // Горизонтальные линии строк остаются свои: их спецификация рисует сама, а перенос «нет
            // линии» сверху и снизу стирал в копии все строчные границы - таблица оставалась одной
            // рамкой. Строка названия - исключение: у неё в оригинале нет ни рамки, ни линии сверху,
            // название просто стоит над таблицей, и без переноса копия обводила его коробкой.
            var hideTop = cell.IsTitle && cell.Source.Borders.Top <= 0;
            var hideBottom = cell.IsTitle && cell.Source.Borders.Bottom <= 0;

            if (hideLeft)
            {
                style.BorderLeftLineStyle = ElementId.InvalidElementId;
            }

            if (hideRight)
            {
                style.BorderRightLineStyle = ElementId.InvalidElementId;
            }

            if (hideTop)
            {
                style.BorderTopLineStyle = ElementId.InvalidElementId;
            }

            if (hideBottom)
            {
                style.BorderBottomLineStyle = ElementId.InvalidElementId;
            }

            style.SetCellStyleOverrideOptions(new TableCellStyleOverrideOptions
            {
                Bold = true,
                Italics = true,
                FontSize = true,
                HorizontalAlignment = true,
                BorderLeftLineStyle = hideLeft,
                BorderRightLineStyle = hideRight,
                BorderTopLineStyle = hideTop,
                BorderBottomLineStyle = hideBottom
            });

            header.SetCellStyle(cell.Row, cell.Column, style);
        });
    }

    private static HorizontalAlignmentStyle Horizontal(CellAlignment alignment)
    {
        return alignment switch
        {
            CellAlignment.Right => HorizontalAlignmentStyle.Right,
            CellAlignment.Center => HorizontalAlignmentStyle.Center,
            _ => HorizontalAlignmentStyle.Left
        };
    }

    private static void TrySet(Action change)
    {
        try
        {
            change();
        }
        catch (RevitException)
        {
            // Размер, объединение или начертание Ревит может отклонить: копия от этого остаётся годной.
        }
    }

    /// <summary>Общая сетка копии: размеры строк и колонок и разложенные по ней ячейки всех секций.</summary>
    private sealed record Layout
    {
        public required IReadOnlyList<double> ColumnWidths { get; init; }
        public required IReadOnlyList<double> RowHeights { get; init; }
        public required IReadOnlyList<LayoutCell> Cells { get; init; }

        /// <summary>
        ///     Секция названия: одна строка на всю ширину таблицы. Колонки в ней Ревит делит поровну, и к
        ///     разбивке таблицы это деление отношения не имеет - по нему сетка получила бы лишние границы,
        ///     а копия - лишнюю колонку. Секция шапки из нескольких строк - другое дело: там таблица
        ///     набрана руками, и её колонки настоящие.
        /// </summary>
        private static bool IsTitleOnly(ScheduleTableSection section)
        {
            return string.Equals(section.SectionName, TitleSectionName, StringComparison.Ordinal)
                   && section.RowHeights.Count <= 1;
        }

        public static Layout Build(ScheduleTableSnapshot snapshot)
        {
            var edges = CollectEdges(snapshot);
            var widths = new List<double>();

            for (var index = 1; index < edges.Count; index++)
            {
                widths.Add(edges[index] - edges[index - 1]);
            }

            var heights = new List<double>();
            var cells = new List<LayoutCell>();

            foreach (var section in snapshot.Sections)
            {
                var rowOffset = heights.Count;

                heights.AddRange(section.RowHeights);

                var title = IsTitleOnly(section);

                foreach (var cell in section.Cells)
                {
                    cells.Add(new LayoutCell
                    {
                        IsTitle = title,
                        Row = rowOffset + cell.Row,
                        LastRow = rowOffset + cell.Row + Math.Max(1, cell.RowSpan) - 1,
                        Column = title ? 0 : ColumnAt(edges, section, cell.Column),
                        LastColumn = title
                            ? Math.Max(0, edges.Count - 2)
                            : ColumnAt(edges, section, cell.Column + Math.Max(1, cell.ColumnSpan)) - 1,
                        Source = cell
                    });
                }
            }

            return WithoutEmptyRows(widths.Count is 0 ? [1.0] : widths, heights, cells);
        }

        /// <summary>
        ///     Убирает строки, в которых нет ни одного слова.
        /// </summary>
        /// <remarks>
        ///     В спецификациях такие строки ставят разделителем - пустая строка после заголовков. В копии
        ///     повторять их незачем: содержимого в них нет, а место и лишнюю линию в сетке они занимают.
        ///     Строка названия остаётся всегда: её текст дописывается позже, из имени оригинала.
        /// </remarks>
        private static Layout WithoutEmptyRows(
            IReadOnlyList<double> widths,
            IReadOnlyList<double> heights,
            IReadOnlyList<LayoutCell> cells)
        {
            var filled = new bool[heights.Count];

            if (filled.Length > 0)
            {
                filled[0] = true;
            }

            foreach (var cell in cells.Where(cell => cell.Source.Text.Trim().Length > 0))
            {
                for (var row = cell.Row; row <= cell.LastRow && row < filled.Length; row++)
                {
                    filled[row] = true;
                }
            }

            var map = new int[heights.Count];
            var kept = new List<double>();

            for (var row = 0; row < heights.Count; row++)
            {
                map[row] = filled[row] ? kept.Count : -1;

                if (filled[row])
                {
                    kept.Add(heights[row]);
                }
            }

            return new Layout
            {
                ColumnWidths = widths,
                RowHeights = kept,
                Cells = cells
                    .Where(cell => cell.Row < map.Length && map[cell.Row] >= 0)
                    .Select(cell => cell with
                    {
                        Row = map[cell.Row],
                        LastRow = LastKept(map, cell.Row, cell.LastRow)
                    })
                    .ToList()
            };
        }

        /// <summary>Последняя оставшаяся строка объединённой области.</summary>
        private static int LastKept(IReadOnlyList<int> map, int row, int lastRow)
        {
            var last = map[row];

            for (var index = row; index <= lastRow && index < map.Count; index++)
            {
                if (map[index] >= 0)
                {
                    last = map[index];
                }
            }

            return last;
        }

        /// <summary>Все вертикальные границы всех секций слева направо, без повторов.</summary>
        private static IReadOnlyList<double> CollectEdges(ScheduleTableSnapshot snapshot)
        {
            var edges = new List<double> { 0.0 };

            foreach (var section in snapshot.Sections)
            {
                if (IsTitleOnly(section))
                {
                    continue;
                }

                var offset = 0.0;

                foreach (var width in section.ColumnWidths)
                {
                    offset += width;

                    if (!edges.Any(edge => Math.Abs(edge - offset) < EdgeTolerance))
                    {
                        edges.Add(offset);
                    }
                }
            }

            edges.Sort();

            return edges;
        }

        /// <summary>Номер колонки сетки, с которой начинается колонка секции.</summary>
        private static int ColumnAt(IReadOnlyList<double> edges, ScheduleTableSection section, int column)
        {
            var offset = section.ColumnWidths.Take(column).Sum();

            for (var index = 0; index < edges.Count; index++)
            {
                if (Math.Abs(edges[index] - offset) < EdgeTolerance)
                {
                    return index;
                }
            }

            return Math.Min(column, Math.Max(0, edges.Count - 2));
        }
    }

    private sealed record LayoutCell
    {
        /// <summary>Ячейка строки названия: у неё своё правило границ.</summary>
        public required bool IsTitle { get; init; }

        public required int Row { get; init; }
        public required int LastRow { get; init; }
        public required int Column { get; init; }
        public required int LastColumn { get; init; }
        public required ScheduleTableCell Source { get; init; }
    }
}

/// <summary>Что получилось из копии спецификации.</summary>
public sealed record ScheduleTableCopyResult
{
    public required ElementId ViewId { get; init; }
    public required string ViewName { get; init; }
    public required int RowCount { get; init; }
    public required int ColumnCount { get; init; }
    public required double WidthInFeet { get; init; }
    public required int SkippedCellCount { get; init; }
}
