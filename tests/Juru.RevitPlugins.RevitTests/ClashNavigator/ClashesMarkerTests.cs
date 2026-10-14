using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

using ClashNavigator.Models.ClashDetection;
using ClashNavigator.Models.Clashes;
using Juru.RevitPlugins.RevitTests.Infrastructure;

using Nice3point.TUnit.Revit;
using Nice3point.TUnit.Revit.Executors;

using TUnit.Core.Executors;

using static Juru.RevitPlugins.RevitTests.Infrastructure.ModelBuilder;

namespace Juru.RevitPlugins.RevitTests.ClashNavigator;

/// <summary>
/// Сравнение прогона поиска коллизий с прошлым (FindClashesCommand): сколько новых, сколько ушло
/// и что переносится со старых. Коллизии строятся на настоящих стенах: сравнение идёт по
/// <see cref="ElementModel"/>, а он читает у элемента категорию, уровень и имя документа.
/// </summary>
public sealed class ClashesMarkerTests : RevitApiTest {
    private Document _document = null!;
    private List<Wall> _walls = null!;

    [Before(Test)]
    [HookExecutor<RevitThreadExecutor>]
    public void CreateWalls() {
        _document = Application.NewProjectDocument(RevitTemplates.Architecture(Application));

        using var transaction = new Transaction(_document, "Стены");
        transaction.Start();
        _walls = Enumerable.Range(0, 4)
            .Select(index => Wall(_document, PointMm(0, index * 1000, 0), PointMm(5000, index * 1000, 0)))
            .ToList();
        transaction.Commit();
    }

    [After(Test)]
    [HookExecutor<RevitThreadExecutor>]
    public void CloseDocument() {
        _document?.Close(false);
    }

    [Test]
    public async Task Counts_SamePairInSwappedOrder_IsNotNew() {
        // Arrange: в новом прогоне стороны коллизии поменялись местами.
        var old = new List<ClashModel> { Clash(0, 1), Clash(1, 2) };
        var fresh = new List<ClashModel> { Clash(1, 0), Clash(2, 3) };

        // Act
        int added = ClashesMarker.CountAdded(fresh, old);
        int gone = ClashesMarker.CountGone(fresh, old);

        // Assert
        using(Assert.Multiple()) {
            await Assert.That(added).IsEqualTo(1);
            await Assert.That(gone).IsEqualTo(1);
        }
    }

    [Test]
    public async Task Counts_SameRun_NothingAddedOrGone() {
        // Arrange
        var old = new List<ClashModel> { Clash(0, 1), Clash(2, 3) };
        var fresh = new List<ClashModel> { Clash(0, 1), Clash(2, 3) };

        // Act
        int added = ClashesMarker.CountAdded(fresh, old);
        int gone = ClashesMarker.CountGone(fresh, old);

        // Assert
        using(Assert.Multiple()) {
            await Assert.That(added).IsEqualTo(0);
            await Assert.That(gone).IsEqualTo(0);
        }
    }

    [Test]
    public async Task MarkSolvedClashes_KnownClash_KeepsStatusNameAndComments() {
        // Arrange
        var known = Clash(0, 1);
        known.ClashStatus = ClashStatus.Analized;
        known.Name = "Коллизия 7";
        known.Comments = [new ClashComment { Body = "Согласовано с КР" }];
        var old = new List<ClashModel> { known };

        var repeated = Clash(1, 0);
        repeated.Name = "Коллизия 1";
        var added = Clash(2, 3);
        added.Name = "Коллизия 2";

        // Act
        var marked = ClashesMarker.MarkSolvedClashes([repeated, added], old).ToList();

        // Assert
        using(Assert.Multiple()) {
            await Assert.That(marked[0].ClashStatus).IsEqualTo(ClashStatus.Analized);
            await Assert.That(marked[0].Name).IsEqualTo("Коллизия 7");
            await Assert.That(marked[0].Comments.Single().Body).IsEqualTo("Согласовано с КР");
            await Assert.That(marked[1].ClashStatus).IsEqualTo(ClashStatus.Active);
            await Assert.That(marked[1].Name).IsEqualTo("Коллизия 2");
        }
    }

    [Test]
    public async Task ElementModel_ReadsWallFromModel() {
        // Act
        var model = new ElementModel(_walls[0]);

        // Assert
        using(Assert.Multiple()) {
            await Assert.That(model.Id).IsEqualTo(_walls[0].Id);
            await Assert.That(model.Category).IsEqualTo(_walls[0].Category.Name);
            await Assert.That(model.Level).IsEqualTo(LowestLevel(_document).Name);
            await Assert.That(model.DocumentName).IsEqualTo(_document.Title);
        }
    }

    private ClashModel Clash(int main, int other) {
        return new ClashModel {
            MainElement = new ElementModel(_walls[main]),
            OtherElement = new ElementModel(_walls[other])
        };
    }
}
