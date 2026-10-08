extern alias ProjectSetup;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.RevitTests.Infrastructure;

using Nice3point.TUnit.Revit;
using Nice3point.TUnit.Revit.Executors;

using ProjectSetup::Juru.RevitPlugins.ProjectSetup.Server;
using ProjectSetup::Juru.RevitPlugins.ProjectSetup.Services;

using TUnit.Core.Executors;

using static Juru.RevitPlugins.RevitTests.Infrastructure.ModelBuilder;

namespace Juru.RevitPlugins.RevitTests.ProjectSetup;

/// <summary>
/// Этапы подготовки проекта (RunProjectSetupJobsCommand) без очереди сервера и окна хода работы:
/// задание собирается здесь, корень проекта - временная папка.
/// </summary>
public sealed class StageExecutorsTests : RevitApiTest {
    private const string SiteName = "OBJ1";

    private TempFolder _folder = null!;

    [Before(Test)]
    public void CreateFolder() {
        _folder = new TempFolder();
    }

    [After(Test)]
    public void DeleteFolder() {
        _folder?.Dispose();
    }

    [Test]
    public async Task StageA_CreatesBasesAndLinksThemIntoGenplan() {
        // Arrange
        string template = RevitTemplates.Architecture(Application);
        var plan = new StageAPayload {
            UncRoot = _folder.Path,
            Genplan = new RevitFileDto { Template = template, Path = $"Genplan_{_folder.Suffix}.rvt" },
            Bases = [
                new BaseFileDto { ObjectId = 1, Template = template, Path = $"Base1_{_folder.Suffix}.rvt" },
                new BaseFileDto { ObjectId = 2, Template = template, Path = $"Base2_{_folder.Suffix}.rvt" }
            ]
        };

        // Act
        var outcome = new StageAExecutor(new RevitFiles(Application)).Execute(plan, new NoProgress());

        // Assert
        using(Assert.Multiple()) {
            await Assert.That(outcome.Status).IsEqualTo("done");
            await Assert.That(outcome.Result.Objects.All(item => item.Ok)).IsTrue();
            await Assert.That(File.Exists(_folder.File(plan.Bases[0].Path))).IsTrue();
            await Assert.That(File.Exists(_folder.File(plan.Bases[1].Path))).IsTrue();
        }

        // Резервная копия .0001 от повторного сохранения только что созданного генплана не нужна.
        string genplanPath = _folder.File(plan.Genplan.Path);
        await Assert.That(File.Exists(Path.ChangeExtension(genplanPath, ".0001.rvt"))).IsFalse();

        var genplan = Open(genplanPath);
        try {
            using(Assert.Multiple()) {
                await Assert.That(RevitFiles.HasLinkTo(genplan, _folder.File(plan.Bases[0].Path))).IsTrue();
                await Assert.That(RevitFiles.HasLinkTo(genplan, _folder.File(plan.Bases[1].Path))).IsTrue();
            }
        } finally {
            genplan.Close(false);
        }
    }

    [Test]
    public async Task StageA_ExistingBase_AsksBeforeChangingAnything() {
        // Arrange
        string template = RevitTemplates.Architecture(Application);
        string existing = $"Base1_{_folder.Suffix}.rvt";
        File.WriteAllText(_folder.File(existing), "");
        var plan = new StageAPayload {
            UncRoot = _folder.Path,
            OnExisting = ConflictModes.Ask,
            Genplan = new RevitFileDto { Template = template, Path = $"Genplan_{_folder.Suffix}.rvt" },
            Bases = [new BaseFileDto { ObjectId = 1, Template = template, Path = existing }]
        };

        // Act
        var outcome = new StageAExecutor(new RevitFiles(Application)).Execute(plan, new NoProgress());

        // Assert
        using(Assert.Multiple()) {
            await Assert.That(outcome.Status).IsEqualTo("needs_confirmation");
            await Assert.That(outcome.Result.Conflicts).Contains(existing);
            await Assert.That(File.Exists(_folder.File(plan.Genplan.Path))).IsFalse();
        }
    }

    [Test]
    public async Task StageA_MissingTemplate_CreatesNothing() {
        // Arrange
        string template = RevitTemplates.Architecture(Application);
        string missing = _folder.File("нет такого шаблона.rte");
        var plan = new StageAPayload {
            UncRoot = _folder.Path,
            Genplan = new RevitFileDto { Template = template, Path = $"Genplan_{_folder.Suffix}.rvt" },
            Bases = [
                new BaseFileDto { ObjectId = 1, Template = template, Path = $"Base1_{_folder.Suffix}.rvt" },
                new BaseFileDto { ObjectId = 2, Template = missing, Path = $"Base2_{_folder.Suffix}.rvt" }
            ]
        };

        // Act
        var outcome = new StageAExecutor(new RevitFiles(Application)).Execute(plan, new NoProgress());

        // Assert: шаблоны проверяются все до первого файла, иначе проект собрался бы наполовину.
        using(Assert.Multiple()) {
            await Assert.That(outcome.Status).IsEqualTo("failed");
            await Assert.That(string.Join("\n", outcome.Log)).Contains(missing);
            await Assert.That(Directory.GetFiles(_folder.Path, "*.rvt")).IsEmpty();
        }
    }

    [Test]
    public async Task StageB_TakesSiteFromBaseAndLinksBase() {
        // Arrange: площадка объекта повёрнута и поднята, как после Publish из генплана.
        var expected = new ProjectPosition(Mm(12000), Mm(-3500), Mm(5500), Math.PI / 6);
        string basePath = $"Base1_{_folder.Suffix}.rvt";
        CreateBaseWithSite(_folder.File(basePath), expected);

        var plan = new StageBPayload {
            UncRoot = _folder.Path,
            Objects = [
                new StageBObjectDto {
                    ObjectId = 1,
                    SiteName = SiteName,
                    BasePath = basePath,
                    Files = [
                        new DisciplineFileDto {
                            Discipline = "HVC",
                            Template = RevitTemplates.Systems(Application),
                            Path = $"HVC_{_folder.Suffix}.rvt"
                        }
                    ]
                }
            ]
        };

        // Act
        var outcome = new StageBExecutor(new RevitFiles(Application)).Execute(plan, new NoProgress());

        // Assert
        using(Assert.Multiple()) {
            await Assert.That(outcome.Status).IsEqualTo("done");
            await Assert.That(outcome.Result.Objects.Single().Ok).IsTrue();
            await Assert.That(outcome.Result.Objects.Single().Created).Contains("HVC");
        }

        var discipline = Open(_folder.File(plan.Objects[0].Files[0].Path));
        try {
            var location = discipline.ActiveProjectLocation;
            var actual = location.GetProjectPosition(XYZ.Zero);
            using(Assert.Multiple()) {
                await Assert.That(location.Name).IsEqualTo(SiteName);
                await Assert.That(ToMm(actual.EastWest)).IsEqualTo(ToMm(expected.EastWest)).Within(0.01);
                await Assert.That(ToMm(actual.NorthSouth)).IsEqualTo(ToMm(expected.NorthSouth)).Within(0.01);
                await Assert.That(ToMm(actual.Elevation)).IsEqualTo(ToMm(expected.Elevation)).Within(0.01);
                await Assert.That(actual.Angle).IsEqualTo(expected.Angle).Within(1e-9);
                await Assert.That(RevitFiles.HasLinkTo(discipline, _folder.File(basePath))).IsTrue();
            }
        } finally {
            discipline.Close(false);
        }
    }

    [Test]
    public async Task StageB_BaseWithoutSite_ReportsErrorAndCreatesNothing() {
        // Arrange: Publish из генплана не делали - площадки объекта в базовом файле нет.
        string basePath = $"Base1_{_folder.Suffix}.rvt";
        var document = Application.NewProjectDocument(RevitTemplates.Architecture(Application));
        document.SaveAs(_folder.File(basePath));
        document.Close(false);

        var plan = new StageBPayload {
            UncRoot = _folder.Path,
            Objects = [
                new StageBObjectDto {
                    ObjectId = 1,
                    SiteName = SiteName,
                    BasePath = basePath,
                    Files = [
                        new DisciplineFileDto {
                            Discipline = "HVC",
                            Template = RevitTemplates.Systems(Application),
                            Path = $"HVC_{_folder.Suffix}.rvt"
                        }
                    ]
                }
            ]
        };

        // Act
        var outcome = new StageBExecutor(new RevitFiles(Application)).Execute(plan, new NoProgress());

        // Assert
        var report = outcome.Result.Objects.Single();
        using(Assert.Multiple()) {
            await Assert.That(outcome.Status).IsEqualTo("failed");
            await Assert.That(report.Ok).IsFalse();
            await Assert.That(report.Error).Contains(SiteName);
            await Assert.That(File.Exists(_folder.File(plan.Objects[0].Files[0].Path))).IsFalse();
        }
    }

    private void CreateBaseWithSite(string path, ProjectPosition position) {
        var document = Application.NewProjectDocument(RevitTemplates.Architecture(Application));
        try {
            using(var transaction = new Transaction(document, "Площадка объекта")) {
                transaction.Start();
                var site = document.ActiveProjectLocation.Duplicate(SiteName);
                site.SetProjectPosition(XYZ.Zero, position);
                transaction.Commit();
            }
            document.SaveAs(path);
        } finally {
            document.Close(false);
        }
    }

    private Document Open(string path) {
        return Application.OpenDocumentFile(ModelPathUtils.ConvertUserVisiblePathToModelPath(path), new OpenOptions());
    }

    private sealed class NoProgress : IJobProgress {
        public List<string> Steps { get; } = [];

        public void Step(int done, int total, string what) {
            Steps.Add(what);
        }
    }
}
