using System.Linq;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.RevitTests.Infrastructure;
using Juru.RevitPlugins.Schedules.Models;
using Juru.RevitPlugins.Schedules.Services;

using Nice3point.TUnit.Revit;
using Nice3point.TUnit.Revit.Executors;

using TUnit.Core.Executors;

using static Juru.RevitPlugins.RevitTests.Infrastructure.ModelBuilder;

namespace Juru.RevitPlugins.RevitTests.Schedules;

/// <summary>
/// Английская копия спецификации (CopyScheduleToDraftingViewCommand) и связь копии с оригиналом,
/// по которой UpdateScheduleCopyCommand находит, что обновлять. Спецификация стен с комментарием,
/// у двух стен комментарий по-русски.
/// </summary>
public sealed class ScheduleCopyTests : RevitApiTest {
    private const string RussianComment = "Наружная стена";
    private const string EnglishComment = "External wall";

    private TempFolder _folder = null!;
    private Document _document = null!;
    private ViewSchedule _schedule = null!;

    [Before(Test)]
    [HookExecutor<RevitThreadExecutor>]
    public void CreateWallSchedule() {
        _folder = new TempFolder();
        _document = Application.NewProjectDocument(RevitTemplates.Architecture(Application));

        using var transaction = new Transaction(_document, "Спецификация стен");
        transaction.Start();
        for(int index = 0; index < 2; index++) {
            var wall = Wall(_document, PointMm(0, index * 1000, 0), PointMm(5000, index * 1000, 0));
            wall.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set(RussianComment);
        }

        _schedule = ViewSchedule.CreateSchedule(_document, new ElementId(BuiltInCategory.OST_Walls));
        _schedule.Name = "Спецификация стен";
        var commentsField = _schedule.Definition.GetSchedulableFields()
            .First(field => field.ParameterId == new ElementId(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS));
        _schedule.Definition.AddField(commentsField);
        transaction.Commit();
    }

    [After(Test)]
    [HookExecutor<RevitThreadExecutor>]
    public void CloseDocument() {
        _document?.Close(false);
        _folder?.Dispose();
    }

    [Test]
    public async Task DraftingCopy_TranslatesByGlossary() {
        // Arrange
        string glossaryPath = _folder.File("glossary.csv");
        GlossaryFile.Write(
            [
                new GlossaryEntry {
                    Key = RussianComment,
                    Readable = RussianComment,
                    Suggestion = EnglishComment,
                    Source = TranslationSource.Human,
                    Occurrences = 2,
                    Schedules = [_schedule.Name]
                }
            ],
            glossaryPath);
        var glossary = Glossary.Load(glossaryPath);

        // Act: шаги команды - экспорт вне транзакции, чтение и отрисовка внутри.
        var exported = new ScheduleTextExporter().TryExport(_schedule, _folder.Path, out _);
        DraftingTableResult result;
        using(var transaction = new Transaction(_document, "Английская копия")) {
            transaction.Start();
            var snapshot = new ScheduleTableReader().Read(_schedule, exported);
            result = new DraftingTableWriter(_document).Draw(snapshot, glossary);
            transaction.Commit();
        }

        // Assert
        var texts = new FilteredElementCollector(_document, result.ViewId)
            .OfClass(typeof(TextNote))
            .Cast<TextNote>()
            .Select(note => note.Text.Trim())
            .ToList();

        using(Assert.Multiple()) {
            await Assert.That(_document.GetElement(result.ViewId)).IsTypeOf<ViewDrafting>();
            await Assert.That(result.ViewName).Contains("(EN)");
            await Assert.That(result.SkippedCellCount).IsEqualTo(0);
            await Assert.That(texts.Count(text => text == EnglishComment)).IsEqualTo(2);
            await Assert.That(texts.Any(text => text.Contains(RussianComment))).IsFalse();
            await Assert.That(glossary.Misses.ContainsKey(RussianComment)).IsFalse();
        }
    }

    [Test]
    public async Task DraftingCopy_WithoutGlossary_KeepsRussianAndReportsMisses() {
        // Arrange: глоссария нет - текст остаётся русским, а строки попадают в отчёт.
        var glossary = Glossary.Load(_folder.File("нет глоссария.csv"));

        // Act
        var exported = new ScheduleTextExporter().TryExport(_schedule, _folder.Path, out _);
        DraftingTableResult result;
        using(var transaction = new Transaction(_document, "Английская копия")) {
            transaction.Start();
            var snapshot = new ScheduleTableReader().Read(_schedule, exported);
            result = new DraftingTableWriter(_document).Draw(snapshot, glossary);
            transaction.Commit();
        }

        // Assert
        var texts = new FilteredElementCollector(_document, result.ViewId)
            .OfClass(typeof(TextNote))
            .Cast<TextNote>()
            .Select(note => note.Text.Trim())
            .ToList();

        using(Assert.Multiple()) {
            await Assert.That(texts.Count(text => text == RussianComment)).IsEqualTo(2);
            await Assert.That(glossary.Misses.ContainsKey(RussianComment)).IsTrue();
        }
    }

    [Test]
    public async Task CopyLink_RememberedCopy_LeadsBackToOriginal() {
        // Arrange
        ViewSchedule copy;
        using(var transaction = new Transaction(_document, "Копия спецификации")) {
            transaction.Start();
            copy = (ViewSchedule) _document.GetElement(_schedule.Duplicate(ViewDuplicateOption.Duplicate));
            ScheduleCopyLink.Remember(copy, _schedule);
            transaction.Commit();
        }

        // Act
        var original = ScheduleCopyLink.TryFindOriginal(_document, copy);
        var copies = ScheduleCopyLink.FindCopies(_document, _schedule);
        var allCopies = ScheduleCopyLink.FindAllCopies(_document);

        // Assert
        using(Assert.Multiple()) {
            await Assert.That(original?.Id).IsEqualTo(_schedule.Id);
            await Assert.That(copies.Select(view => view.Id)).IsEquivalentTo([copy.Id]);
            await Assert.That(allCopies.Select(view => view.Id)).IsEquivalentTo([copy.Id]);
            await Assert.That(ScheduleCopyLink.TryFindOriginal(_document, _schedule)).IsNull();
        }
    }
}
