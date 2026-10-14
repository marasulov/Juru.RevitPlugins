using System.Collections.Generic;
using System.IO;
using System.Linq;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Core.FileNaming;
using Juru.RevitPlugins.Openings.Models.Configs;
using Juru.RevitPlugins.Openings.Models.Intersections;
using Juru.RevitPlugins.Openings.Models.Placement;
using Juru.RevitPlugins.RevitTests.Infrastructure;

using Nice3point.TUnit.Revit;
using Nice3point.TUnit.Revit.Executors;

using TUnit.Core.Executors;

using static Juru.RevitPlugins.RevitTests.Infrastructure.ModelBuilder;

namespace Juru.RevitPlugins.RevitTests.Openings;

/// <summary>
/// Выдача заданий на отверстия (PlaceOpeningTasksCommand) без окна: те же шаги, что у команды,
/// на файле ВИС со связью КР. Стена в связи идёт по оси X от 0 до 10 м, высотой 3 м.
/// </summary>
public sealed class OpeningTasksPipelineTests : RevitApiTest {
    private TempFolder _folder = null!;
    private Document _host = null!;
    private OpeningsConfig _config = null!;

    [Before(Test)]
    [HookExecutor<RevitThreadExecutor>]
    public void CreateHostWithStructureLink() {
        _folder = new TempFolder();
        _config = new OpeningsConfig();

        // Дисциплина связи узнаётся по фрагменту "_KR" в имени файла.
        string structurePath = CreateStructureFile($"Str_KR_{_folder.Suffix}.rvt");

        _host = Application.NewProjectDocument(RevitTemplates.Systems(Application));
        using var transaction = new Transaction(_host, "Связь КР");
        transaction.Start();
        Link(_host, structurePath);
        transaction.Commit();
    }

    [After(Test)]
    [HookExecutor<RevitThreadExecutor>]
    public void CloseHost() {
        _host?.Close(false);
        _folder?.Dispose();
    }

    /// <summary>
    /// Сечение воздуховода 400x200 известно точно, поэтому к нему прибавляется зазор с каждой
    /// стороны без округления вверх.
    /// </summary>
    [Test]
    [Arguments(50, 500, 300)]
    [Arguments(100, 600, 400)]
    public async Task Duct_ThroughWall_GivesOpeningWithOffsetOnEachSide(
        double offsetMm, double expectedWidthMm, double expectedHeightMm) {

        // Arrange
        _config.GetCategory(MepKind.RectangleDuct).Offsets.Single().OffsetMm = offsetMm;
        AddDuct(PointMm(5000, -2000, 1500), PointMm(5000, 2000, 1500), 400, 200);

        // Act
        var run = RunPipeline();

        // Assert
        using(Assert.Multiple()) {
            await Assert.That(run.Intersections.Count).IsEqualTo(1);
            await Assert.That(run.Validation.Accepted.Count).IsEqualTo(1);
            await Assert.That(run.Planned.Count).IsEqualTo(1);
        }

        var size = run.Planned.Single().GetSize();
        using(Assert.Multiple()) {
            await Assert.That(size.OpeningType).IsEqualTo(OpeningType.WallRectangle);
            await Assert.That(ToMm(size.Width)).IsEqualTo(expectedWidthMm).Within(0.5);
            await Assert.That(ToMm(size.Height)).IsEqualTo(expectedHeightMm).Within(0.5);
        }
    }

    [Test]
    public async Task Duct_AlongWall_IsRejectedWithReason() {
        // Arrange: воздуховод лежит внутри стены по её оси.
        AddDuct(PointMm(1000, 0, 1500), PointMm(4000, 0, 1500), 100, 100);

        // Act
        var run = RunPipeline();

        // Assert
        using(Assert.Multiple()) {
            await Assert.That(run.Validation.Accepted.Count).IsEqualTo(0);
            await Assert.That(run.Validation.Rejected.Count).IsEqualTo(1);
            await Assert.That(run.Validation.Rejected.Single().Reason).Contains("вдоль стены");
        }
    }

    [Test]
    public async Task Duct_SmallerThanMinimum_IsNotSearched() {
        // Arrange: порог по умолчанию - 50 мм по каждой стороне сечения.
        AddDuct(PointMm(5000, -2000, 1500), PointMm(5000, 2000, 1500), 40, 40);

        // Act
        var run = RunPipeline();

        // Assert
        using(Assert.Multiple()) {
            await Assert.That(run.Candidates.Count).IsEqualTo(0);
            await Assert.That(run.Intersections.Count).IsEqualTo(0);
        }
    }

    [Test]
    public async Task Duct_BesideWall_GivesNoIntersection() {
        // Arrange
        AddDuct(PointMm(5000, 1000, 1500), PointMm(5000, 3000, 1500), 400, 200);

        // Act
        var run = RunPipeline();

        // Assert
        using(Assert.Multiple()) {
            await Assert.That(run.Candidates.Count).IsEqualTo(1);
            await Assert.That(run.Intersections.Count).IsEqualTo(0);
        }
    }

    [Test]
    public async Task Link_WithUnknownDiscipline_IsReportedAndNotSearched() {
        // Arrange: по имени этой связи дисциплину не узнать.
        string otherPath = CreateStructureFile($"Model_{_folder.Suffix}.rvt");
        using(var transaction = new Transaction(_host, "Вторая связь")) {
            transaction.Start();
            Link(_host, otherPath);
            transaction.Commit();
        }

        // Act
        var links = new StructureLinksProvider(_host, _config);
        var providers = links.GetProviders();
        var unrecognized = links.GetUnrecognizedLinkNames();

        // Assert
        using(Assert.Multiple()) {
            await Assert.That(providers.Count).IsEqualTo(1);
            await Assert.That(providers.Single().Discipline).IsEqualTo(Discipline.STR);
            await Assert.That(unrecognized.Count).IsEqualTo(1);
            await Assert.That(unrecognized.Single()).Contains("Model_");
        }
    }

    /// <summary>
    /// Семейства заданий берутся из библиотеки института на общем диске; без неё тест пропускается.
    /// </summary>
    [Test]
    public async Task Place_ThenPlaceAgain_KeepsOneTaskWithSize() {
        // Arrange
        string familyPath = Path.Combine(_config.FamilyLibraryPath, _config.Naming.WallRectangleFamily + ".rfa");
        if(!File.Exists(familyPath)) {
            Skip.Test($"Нет библиотеки семейств заданий: {familyPath}");
        }

        using(var transaction = new Transaction(_host, "Семейство задания")) {
            transaction.Start();
            _host.LoadFamily(familyPath, out _);
            transaction.Commit();
        }

        // Семейство уже в проекте. Без пути к библиотеке плагин не покажет окно о догрузке,
        // а нехватку параметров вернёт в отчёте.
        _config.FamilyLibraryPath = null!;
        AddDuct(PointMm(5000, -2000, 1500), PointMm(5000, 2000, 1500), 400, 200);

        // Act
        var first = new OpeningTaskPlacer(_host, _config).Place(RunPipeline().Planned);
        var second = new OpeningTaskPlacer(_host, _config).Place(RunPipeline().Planned);

        // Assert
        var tasks = new FilteredElementCollector(_host)
            .OfClass(typeof(FamilyInstance))
            .Cast<FamilyInstance>()
            .Where(instance => instance.Symbol.FamilyName == _config.Naming.WallRectangleFamily)
            .ToList();

        using(Assert.Multiple()) {
            await Assert.That(first.Placed.Count).IsEqualTo(1);
            await Assert.That(first.Failed.Count).IsEqualTo(0);
            await Assert.That(first.MissingParameters.Count).IsEqualTo(0);
            await Assert.That(second.Placed.Count).IsEqualTo(0);
            await Assert.That(second.Failed.Count).IsEqualTo(0);
            await Assert.That(tasks.Count).IsEqualTo(1);
        }

        var width = tasks.Single().LookupParameter(_config.Naming.WidthParam);
        await Assert.That(ToMm(width.AsDouble())).IsEqualTo(500).Within(0.5);
    }

    private string CreateStructureFile(string fileName) {
        string path = _folder.File(fileName);
        var document = Application.NewProjectDocument(RevitTemplates.Architecture(Application));
        try {
            using(var transaction = new Transaction(document, "Стена")) {
                transaction.Start();
                Wall(document, PointMm(0, 0, 0), PointMm(10000, 0, 0));
                transaction.Commit();
            }
            document.SaveAs(path);
        } finally {
            // Открытый файл не подгрузить связью.
            document.Close(false);
        }

        return path;
    }

    private static void Link(Document host, string path) {
        var load = RevitLinkType.Create(
            host, ModelPathUtils.ConvertUserVisiblePathToModelPath(path), new RevitLinkOptions(false));
        RevitLinkInstance.Create(host, load.ElementId);
    }

    private void AddDuct(XYZ start, XYZ end, double widthMm, double heightMm) {
        using var transaction = new Transaction(_host, "Воздуховод");
        transaction.Start();
        RectangularDuct(_host, start, end, widthMm, heightMm);
        transaction.Commit();
    }

    /// <summary>
    /// Шаги PlaceOpeningTasksCommand до расстановки, в том же порядке.
    /// </summary>
    private PipelineRun RunPipeline() {
        var structures = new StructureLinksProvider(_host, _config).GetProviders();
        var mep = new MepElementsProvider(_host, _config);
        var intersections = new SolidIntersectionFinder(mep).Find(structures);
        var validation = new IntersectionValidator(_config).Validate(intersections);

        var calculator = new OpeningSizeCalculator();
        var planned = validation.Accepted
            .Select(calculator.Calculate)
            .Where(opening => opening is not null)
            .ToList();

        return new PipelineRun(
            mep.GetCandidates(), intersections, validation, new OpeningMerger(_config).Merge(planned));
    }

    private sealed record PipelineRun(
        IReadOnlyCollection<MepElementCandidate> Candidates,
        IReadOnlyCollection<MepStructureIntersection> Intersections,
        IntersectionValidator.ValidationResult Validation,
        IReadOnlyCollection<PlannedOpening> Planned);
}
