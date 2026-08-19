using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Juru.RevitPlugins.Schedules.Models;

using RevitException = Autodesk.Revit.Exceptions.ApplicationException;

namespace Juru.RevitPlugins.Schedules.Services;

/// <summary>
///     Копирует в чертёжные виды все спецификации, размещённые на листах, и вставляет каждую копию на лист
///     рядом с оригиналом. Прогон идемпотентный: копии прошлых прогонов удаляются. Каждая спецификация
///     обрабатывается отдельной транзакцией, поэтому сбой на одной не отменяет остальные.
/// </summary>
public sealed class SheetScheduleCopyService(UIDocument uiDocument)
{
    private const double MillimetersPerFoot = 304.8;

    /// <summary>Зазор между оригиналом и копией на листе.</summary>
    private const double PlacementGap = 10.0 / MillimetersPerFoot;

    private const string CopySuffix = " (EN)";

    private readonly Document document = uiDocument.Document;

    /// <param name="cellDumpDirectory">
    ///     Папка, куда складывается прочитанный текст ячеек по каждой спецификации. Пусто — выгрузка не нужна.
    /// </param>
    public IReadOnlyList<SheetScheduleCopy> CopyAll(Glossary glossary, string? cellDumpDirectory)
    {
        if (glossary is null)
        {
            throw new ArgumentNullException(nameof(glossary));
        }

        // Активной может быть копия прошлого прогона: её удаление Revit запретит, а ссылка на неё после
        // удаления станет недействительной. Поэтому уходим с копии заранее и запоминаем вид по номеру.
        LeaveCopyView();

        var originalViewId = uiDocument.ActiveView.Id;

        var copies = new SheetScheduleFinder(document)
            .Find()
            .Select(entry => Copy(entry.Schedule, entry.Placements, glossary, cellDumpDirectory))
            .ToList();

        TryActivate(originalViewId);

        return copies;
    }

    private SheetScheduleCopy Copy(
        ViewSchedule schedule,
        IReadOnlyList<SheetPlacement> placements,
        Glossary glossary,
        string? cellDumpDirectory)
    {
        var definition = schedule.Definition;
        var fields = definition.GetFieldOrder()
            .Select(definition.GetField)
            .ToList();

        // Экспорт пишет файл на диск и обязан идти до открытия транзакции.
        var exportedText = new ScheduleTextExporter()
            .TryExport(schedule, Path.Combine(cellDumpDirectory ?? Path.GetTempPath(), "export"), out var exportWarning);

        var described = new SheetScheduleCopy
        {
            ScheduleName = schedule.Name,
            SheetNumbers = placements.Select(placement => placement.SheetNumber).ToList(),
            VisibleColumnHeadings = fields.Where(field => !field.IsHidden).Select(DescribeColumn).ToList(),
            HiddenFieldNames = fields.Where(field => field.IsHidden).Select(field => field.GetName()).ToList(),
            IsSplit = schedule.IsSplit(),
            ReplacedCopyCount = DeletePreviousCopies(schedule.Name),
            RowCountBeforeActivation = ReadBodyRowCount(schedule),
            WasActivated = TryActivate(schedule),
            ExportedRowCount = exportedText?.Count ?? 0,
            ExportWarning = exportWarning
        };

        using var transaction = new Transaction(document, $"Копия спецификации: {schedule.Name}");
        transaction.Start();

        try
        {
            var snapshot = new ScheduleTableReader(document).Read(schedule, exportedText);
            var writer = new DraftingTableWriter(document);
            var result = writer.Draw(snapshot, glossary);
            var placement = Place(result, placements);

            if (cellDumpDirectory is not null)
            {
                ScheduleTableDiagnosticsWriter.WriteTo(snapshot, writer.AppliedViewScale, cellDumpDirectory);
            }

            transaction.Commit();

            var body = snapshot.Sections.LastOrDefault();

            return described with
            {
                ViewName = result.ViewName,
                RowCount = body?.RowHeights.Count ?? 0,
                TableWidthMillimeters = result.WidthInFeet * MillimetersPerFoot,
                LineCount = result.LineCount,
                TextNoteCount = result.TextNoteCount,
                SkippedCellCount = result.SkippedCellCount,
                PlacedOnSheet = placement.SheetNumber,
                NotPlacedOnSheets = placement.SkippedSheetNumbers,
                PlacementError = placement.Error
            };
        }
        catch (RevitException exception)
        {
            transaction.RollBack();

            return described with { Error = exception.Message };
        }
    }

    /// <summary>
    ///     Вставляет копию на первый лист оригинала правее него. Чертёжный вид размещается только на одном
    ///     листе, поэтому остальные листы возвращаются как пропущенные.
    /// </summary>
    private PlacementResult Place(DraftingTableResult result, IReadOnlyList<SheetPlacement> placements)
    {
        if (placements.Count is 0)
        {
            return new PlacementResult(null, [], null);
        }

        var target = placements[0];
        var skipped = placements.Skip(1).Select(placement => placement.SheetNumber).ToList();

        if (!Viewport.CanAddViewToSheet(document, target.SheetId, result.ViewId))
        {
            return new PlacementResult(null, skipped, "Revit не разрешает разместить вид на этом листе.");
        }

        try
        {
            // Спецификация привязана к левому верхнему углу, видовой экран - к своему центру.
            var center = new XYZ(
                target.Point.X + result.WidthInFeet + PlacementGap + result.WidthInFeet / 2,
                target.Point.Y - result.HeightInFeet / 2,
                0);

            Viewport.Create(document, target.SheetId, result.ViewId, center);

            return new PlacementResult(target.SheetNumber, skipped, null);
        }
        catch (RevitException exception)
        {
            return new PlacementResult(null, skipped, exception.Message);
        }
    }

    /// <summary>
    ///     Удаляет чертёжные виды прошлых прогонов, чтобы копии не накапливались. Удаление вида убирает и его
    ///     видовой экран с листа.
    /// </summary>
    private int DeletePreviousCopies(string scheduleName)
    {
        var baseName = scheduleName + CopySuffix;

        var stale = new FilteredElementCollector(document)
            .OfClass(typeof(ViewDrafting))
            .Cast<ViewDrafting>()
            .Where(view => IsPreviousCopy(view.Name, baseName))
            .Select(view => view.Id)
            .ToList();

        if (stale.Count is 0)
        {
            return 0;
        }

        using var transaction = new Transaction(document, $"Удаление прежних копий: {scheduleName}");
        transaction.Start();

        try
        {
            document.Delete(stale);
            transaction.Commit();

            return stale.Count;
        }
        catch (RevitException)
        {
            transaction.RollBack();

            return 0;
        }
    }

    /// <summary>
    ///     Копией считается вид с точным именем либо он же с номером, который дописывается при совпадении имён.
    /// </summary>
    private static bool IsPreviousCopy(string viewName, string baseName)
    {
        if (string.Equals(viewName, baseName, StringComparison.CurrentCultureIgnoreCase))
        {
            return true;
        }

        if (!viewName.StartsWith(baseName + " ", StringComparison.CurrentCultureIgnoreCase))
        {
            return false;
        }

        return viewName[(baseName.Length + 1)..].All(char.IsDigit);
    }

    /// <summary>
    ///     Переводит активный вид с копии прошлого прогона на любой лист: удалить активный вид Revit не даёт.
    /// </summary>
    private void LeaveCopyView()
    {
        if (uiDocument.ActiveView is not ViewDrafting drafting)
        {
            return;
        }

        if (!drafting.Name.EndsWith(CopySuffix, StringComparison.CurrentCultureIgnoreCase)
            && !drafting.Name.Contains(CopySuffix + " "))
        {
            return;
        }

        var sheet = new FilteredElementCollector(document)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>()
            .FirstOrDefault(candidate => !candidate.IsTemplate);

        if (sheet is not null)
        {
            TryActivate(sheet.Id);
        }
    }

    /// <summary>
    ///     Тело спецификации считается по требованию, и у закрытого вида остаётся устаревшим. Открытие вида
    ///     заставляет Revit его пересчитать. Смена активного вида недопустима внутри транзакции.
    /// </summary>
    private bool TryActivate(ElementId viewId)
    {
        if (document.GetElement(viewId) is not View view || !view.IsValidObject)
        {
            return false;
        }

        return TryActivate(view);
    }

    /// <summary>
    ///     Возврат к прежнему виду - удобство, а не часть работы: его отказ не должен обрывать пакетный
    ///     прогон, а Revit сообщает о недействительном виде исключением из слоя взаимодействия, которое
    ///     не наследуется от управляемых исключений API.
    /// </summary>
    private bool TryActivate(View view)
    {
        if (!view.IsValidObject)
        {
            return false;
        }

        try
        {
            uiDocument.ActiveView = view;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private int ReadBodyRowCount(ViewSchedule schedule)
    {
        try
        {
            return schedule.GetTableData().GetSectionData(SectionType.Body).NumberOfRows;
        }
        catch (RevitException)
        {
            return 0;
        }
    }

    private static string DescribeColumn(ScheduleField field)
    {
        var heading = field.ColumnHeading;

        return string.IsNullOrWhiteSpace(heading) ? field.GetName() : heading;
    }

    private sealed record PlacementResult(
        string? SheetNumber,
        IReadOnlyList<string> SkippedSheetNumbers,
        string? Error);
}
