using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Schedules.Models;

using RevitException = Autodesk.Revit.Exceptions.ApplicationException;

namespace Juru.RevitPlugins.Schedules.Services;

/// <summary>
///     Рисует снимок спецификации заново в чертёжном виде: сетку — линиями детализации, текст — примечаниями.
///     Вид создаётся в масштабе 1:1, поэтому размеры в футах совпадают с печатными.
/// </summary>
public sealed class DraftingTableWriter(Document document)
{
    private const double MillimetersPerFoot = 304.8;
    private const double CellPadding = 0.5 / MillimetersPerFoot;
    private const double MinCurveLength = 0.005;

    /// <summary>
    ///     Колонка или строка тоньше этого значения не даёт отдельной границы. Это колонки-распорки: в
    ///     обмеренных спецификациях они шириной 3 мм, тогда как самая узкая содержательная колонка - 10 мм,
    ///     и текст высотой 2,5 мм в такую полосу не помещается.
    /// </summary>
    private const double DegenerateSize = 5.0 / MillimetersPerFoot;

    private const string TitleSectionName = "Header";

    // Пределы Revit для высоты текста: 0,2274-372,4623 мм.
    private const double MinTextSize = 0.5 / MillimetersPerFoot;
    private const double MaxTextSize = 100.0 / MillimetersPerFoot;

    /// <summary>
    ///     Стиль ячейки отдаёт высоту текста в пикселях при 96 DPI: 96 пикселей на дюйм, 12 дюймов в футе.
    ///     Значение 9,448819 соответствует 2,5 мм.
    /// </summary>
    private const double PixelsPerFoot = 96.0 * 12.0;

    private readonly Dictionary<string, ElementId> textNoteTypes = [];

    /// <summary>Масштаб, который вид принял фактически: заданное значение может не примениться.</summary>
    public int AppliedViewScale { get; private set; }

    private string? textSizeNote;
    private int lineCount;
    private int textNoteCount;
    private int skippedCellCount;
    private string? firstSkipReason;

    /// <summary>
    ///     Создаёт чертёжный вид и заполняет его. Метод обязан выполняться внутри открытой транзакции.
    /// </summary>
    public DraftingTableResult Draw(ScheduleTableSnapshot snapshot, Glossary glossary)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        if (glossary is null)
        {
            throw new ArgumentNullException(nameof(glossary));
        }

        var view = CreateView(snapshot.ScheduleName);
        var sectionTop = 0.0;
        var width = 0.0;

        foreach (var section in snapshot.Sections)
        {
            var drawnHeight = DrawSection(view, section, sectionTop, glossary);

            if (drawnHeight > 0)
            {
                width = Math.Max(width, section.ColumnWidths.Sum());
            }

            sectionTop -= drawnHeight;
        }

        return new DraftingTableResult
        {
            ViewName = view.Name,
            ViewId = view.Id,
            WidthInFeet = width,
            HeightInFeet = -sectionTop,
            LineCount = lineCount,
            TextNoteCount = textNoteCount,
            SkippedCellCount = skippedCellCount,
            FirstSkipReason = firstSkipReason,
            TextSizeNote = textSizeNote
        };
    }

    private ViewDrafting CreateView(string scheduleName)
    {
        var draftingTypeId = new FilteredElementCollector(document)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .First(viewFamilyType => viewFamilyType.ViewFamily is ViewFamily.Drafting)
            .Id;

        var view = ViewDrafting.Create(document, draftingTypeId);

        // Текст печатается в бумажном размере независимо от масштаба, а линии - нет. Масштаб 1:1 уравнивает их.
        view.Scale = 1;
        document.Regenerate();
        AppliedViewScale = view.Scale;

        view.Name = FindFreeViewName($"{scheduleName} (EN)");

        return view;
    }

    private string FindFreeViewName(string preferredName)
    {
        var taken = new FilteredElementCollector(document)
            .OfClass(typeof(View))
            .Cast<View>()
            .Select(view => view.Name)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);

        if (!taken.Contains(preferredName))
        {
            return preferredName;
        }

        for (var attempt = 2; ; attempt++)
        {
            var candidate = $"{preferredName} {attempt}";

            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    ///     Рисует секцию и возвращает её фактическую высоту. Ширина сохраняется как в оригинале, а строки
    ///     раздвигаются вниз под текст: сначала текст создаётся, затем Revit сообщает его высоту, и только
    ///     после этого считается сетка. Секция без текста не рисуется — пустая строка заголовка иначе даёт
    ///     лишнюю рамку.
    /// </summary>
    private double DrawSection(View view, ScheduleTableSection section, double sectionTop, Glossary glossary)
    {
        var texts = section.Cells.ToDictionary(cell => cell, cell => glossary.Translate(cell.Text));

        if (texts.Values.All(string.IsNullOrWhiteSpace))
        {
            return 0;
        }

        var columnX = BuildOffsets(section.ColumnWidths, 0, 1);
        var placed = CreateCellTexts(view, section, columnX, texts, sectionTop);

        document.Regenerate();

        var rowHeights = MeasureRowHeights(section, placed);
        var rowY = BuildOffsets(rowHeights, sectionTop, -1);

        // Секция заголовка - это область названия спецификации: внутренних границ у неё нет, а деление на
        // колонки Revit отдаёт равными долями, не имеющими отношения к разбивке таблицы.
        var outlineOnly = string.Equals(section.SectionName, TitleSectionName, StringComparison.Ordinal);

        AlignCellTexts(placed, rowY);
        DrawVerticalLines(view, section, columnX, rowY, outlineOnly);
        DrawHorizontalLines(view, section, columnX, rowY, rowHeights, outlineOnly);

        return rowHeights.Sum();
    }

    /// <summary>
    ///     Высота строки — заданная в спецификации либо необходимая для размещённого текста, что больше.
    ///     Текст, растянутый на несколько строк, высоту не увеличивает: он в них и так укладывается.
    /// </summary>
    private static double[] MeasureRowHeights(ScheduleTableSection section, IReadOnlyList<PlacedCellText> placed)
    {
        var heights = section.RowHeights.ToArray();

        foreach (var item in placed)
        {
            if (item.Cell.RowSpan is not 1)
            {
                continue;
            }

            var required = item.Note.Height + 2 * CellPadding;

            if (required > heights[item.Cell.Row])
            {
                heights[item.Cell.Row] = required;
            }
        }

        return heights;
    }

    private void AlignCellTexts(IReadOnlyList<PlacedCellText> placed, double[] rowY)
    {
        foreach (var item in placed)
        {
            var target = rowY[item.Cell.Row] - CellPadding;
            var offset = target - item.Note.Coord.Y;

            if (Math.Abs(offset) < MinCurveLength / 100)
            {
                continue;
            }

            ElementTransformUtils.MoveElement(document, item.Note.Id, new XYZ(0, offset, 0));
        }
    }

    private static double[] BuildOffsets(IReadOnlyList<double> sizes, double start, int direction)
    {
        var offsets = new double[sizes.Count + 1];
        offsets[0] = start;

        for (var index = 0; index < sizes.Count; index++)
        {
            offsets[index + 1] = offsets[index] + direction * sizes[index];
        }

        return offsets;
    }

    /// <summary>
    ///     Границу между соседними ячейками рисует только там, где они не входят в одно объединение.
    ///     Подряд идущие участки одной границы сливаются в одну линию.
    /// </summary>
    private void DrawVerticalLines(
        View view,
        ScheduleTableSection section,
        double[] columnX,
        double[] rowY,
        bool outlineOnly)
    {
        var rowCount = section.RowHeights.Count;
        var columnCount = section.ColumnWidths.Count;

        for (var boundary = 0; boundary <= columnCount; boundary++)
        {
            var runStart = -1;

            for (var row = 0; row < rowCount; row++)
            {
                var exists = boundary is 0
                             || boundary == columnCount
                             || (!outlineOnly
                                 && !IsDegenerateNeighbour(section.ColumnWidths, boundary)
                                 && section.MergeAnchors[row][boundary - 1] != section.MergeAnchors[row][boundary]);

                if (exists && runStart < 0)
                {
                    runStart = row;
                }

                if (!exists && runStart >= 0)
                {
                    DrawLine(view, columnX[boundary], rowY[runStart], columnX[boundary], rowY[row]);
                    runStart = -1;
                }
            }

            if (runStart >= 0)
            {
                DrawLine(view, columnX[boundary], rowY[runStart], columnX[boundary], rowY[rowCount]);
            }
        }
    }

    private void DrawHorizontalLines(
        View view,
        ScheduleTableSection section,
        double[] columnX,
        double[] rowY,
        double[] rowHeights,
        bool outlineOnly)
    {
        var rowCount = rowHeights.Length;
        var columnCount = section.ColumnWidths.Count;

        for (var boundary = 0; boundary <= rowCount; boundary++)
        {
            var runStart = -1;

            for (var column = 0; column < columnCount; column++)
            {
                var exists = boundary is 0
                             || boundary == rowCount
                             || (!outlineOnly
                                 && !IsDegenerateNeighbour(rowHeights, boundary)
                                 && section.MergeAnchors[boundary - 1][column] != section.MergeAnchors[boundary][column]);

                if (exists && runStart < 0)
                {
                    runStart = column;
                }

                if (!exists && runStart >= 0)
                {
                    DrawLine(view, columnX[runStart], rowY[boundary], columnX[column], rowY[boundary]);
                    runStart = -1;
                }
            }

            if (runStart >= 0)
            {
                DrawLine(view, columnX[runStart], rowY[boundary], columnX[columnCount], rowY[boundary]);
            }
        }
    }

    /// <summary>
    ///     Граница между вырожденной полосой и её соседом не рисуется: остаётся только внешняя граница полосы.
    /// </summary>
    private static bool IsDegenerateNeighbour(IReadOnlyList<double> sizes, int boundary)
    {
        return sizes[boundary - 1] < DegenerateSize;
    }

    private void DrawLine(View view, double startX, double startY, double endX, double endY)
    {
        var start = new XYZ(startX, startY, 0);
        var end = new XYZ(endX, endY, 0);

        if (start.DistanceTo(end) < MinCurveLength)
        {
            return;
        }

        document.Create.NewDetailCurve(view, Line.CreateBound(start, end));
        lineCount++;
    }

    /// <summary>
    ///     Создаёт текст всех ячеек на общей высоте: окончательное положение задаётся после измерения строк.
    /// </summary>
    private List<PlacedCellText> CreateCellTexts(
        View view,
        ScheduleTableSection section,
        double[] columnX,
        IReadOnlyDictionary<ScheduleTableCell, string> texts,
        double sectionTop)
    {
        var placed = new List<PlacedCellText>();
        var columnCount = section.ColumnWidths.Count;

        foreach (var cell in section.Cells)
        {
            var text = texts[cell];

            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var left = columnX[cell.Column];
            var right = columnX[Math.Min(cell.Column + cell.ColumnSpan, columnCount)];

            var note = PlaceText(view, cell, text, left, right, sectionTop - CellPadding);

            if (note is not null)
            {
                placed.Add(new PlacedCellText(cell, note));
            }
        }

        return placed;
    }

    /// <summary>
    ///     Размещает текст с переносом по ширине ячейки. Ячейка уже или шире допустимой для примечания
    ///     ширины получает текст без переноса, привязанный по своему выравниванию.
    /// </summary>
    private TextNote? PlaceText(View view, ScheduleTableCell cell, string text, double left, double right, double top)
    {
        var options = new TextNoteOptions
        {
            TypeId = ResolveTextNoteTypeId(cell),
            HorizontalAlignment = MapAlignment(cell.Alignment)
        };

        var width = right - left - 2 * CellPadding;
        var minWidth = TextElement.GetMinimumAllowedWidth(document, options.TypeId);
        var maxWidth = TextElement.GetMaximumAllowedWidth(document, options.TypeId);

        // Точка привязки примечания соответствует его выравниванию, а не левому краю.
        var anchor = new XYZ(ResolveAnchorX(cell.Alignment, left, right), top, 0);

        try
        {
            var note = width >= minWidth && width <= maxWidth
                ? TextNote.Create(document, view.Id, anchor, width, text, options)
                : TextNote.Create(document, view.Id, anchor, text, options);

            textNoteCount++;

            return note;
        }
        catch (RevitException exception)
        {
            skippedCellCount++;
            firstSkipReason ??= $"[{cell.Row};{cell.Column}] {exception.Message}";

            return null;
        }
    }

    private static double ResolveAnchorX(CellAlignment alignment, double left, double right)
    {
        return alignment switch
        {
            CellAlignment.Center => (left + right) / 2,
            CellAlignment.Right => right - CellPadding,
            _ => left + CellPadding
        };
    }

    private sealed record PlacedCellText(ScheduleTableCell Cell, TextNote Note);

    private static HorizontalTextAlignment MapAlignment(CellAlignment alignment)
    {
        return alignment switch
        {
            CellAlignment.Center => HorizontalTextAlignment.Center,
            CellAlignment.Right => HorizontalTextAlignment.Right,
            _ => HorizontalTextAlignment.Left
        };
    }

    /// <summary>
    ///     Подбирает тип текста под размер и начертание ячейки, создавая его при первом обращении.
    /// </summary>
    private ElementId ResolveTextNoteTypeId(ScheduleTableCell cell)
    {
        var defaultTypeId = document.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
        var defaultType = (TextNoteType)document.GetElement(defaultTypeId);
        var defaultSize = defaultType.get_Parameter(BuiltInParameter.TEXT_SIZE)?.AsDouble() ?? MinTextSize;

        var size = ResolveTextSize(cell.TextSize, defaultSize);
        var typeName = BuildTypeName(size, cell.IsBold, cell.IsItalic);

        if (textNoteTypes.TryGetValue(typeName, out var cached))
        {
            return cached;
        }

        var existing = new FilteredElementCollector(document)
            .OfClass(typeof(TextNoteType))
            .Cast<TextNoteType>()
            .FirstOrDefault(type => string.Equals(type.Name, typeName, StringComparison.CurrentCulture));

        if (existing is not null)
        {
            textNoteTypes[typeName] = existing.Id;
            return existing.Id;
        }

        var created = (TextNoteType)defaultType.Duplicate(typeName);

        if (created.get_Parameter(BuiltInParameter.TEXT_SIZE)?.Set(size) is false)
        {
            textSizeNote ??= $"Revit отклонил высоту текста {ToMillimeters(size)} мм "
                             + $"(из стиля ячейки пришло {cell.TextSize:0.#####}).";
        }

        created.get_Parameter(BuiltInParameter.TEXT_STYLE_BOLD)?.Set(cell.IsBold ? 1 : 0);
        created.get_Parameter(BuiltInParameter.TEXT_STYLE_ITALIC)?.Set(cell.IsItalic ? 1 : 0);

        textNoteTypes[typeName] = created.Id;

        return created.Id;
    }

    private static double ResolveTextSize(double styleTextSize, double defaultSize)
    {
        if (styleTextSize <= 0)
        {
            return defaultSize;
        }

        return Math.Min(Math.Max(styleTextSize / PixelsPerFoot, MinTextSize), MaxTextSize);
    }

    private static string ToMillimeters(double feet)
    {
        return (feet * MillimetersPerFoot).ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string BuildTypeName(double size, bool isBold, bool isItalic)
    {
        var millimeters = ToMillimeters(size);
        var weight = isBold ? " Bold" : string.Empty;
        var slant = isItalic ? " Italic" : string.Empty;

        return $"JuruEn {millimeters}mm{weight}{slant}";
    }
}
