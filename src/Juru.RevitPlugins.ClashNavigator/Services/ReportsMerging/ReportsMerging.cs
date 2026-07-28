using System;
using System.Collections.Generic;
using System.Linq;

using dosymep.SimpleServices;

using Juru.RevitPlugins.ClashNavigator.Models.Clashes;
using Juru.RevitPlugins.ClashNavigator.ViewModels.Navigator;

namespace Juru.RevitPlugins.ClashNavigator.Services.ReportsMerging;

/// <summary>Groups loaded reports and calculates the report-name intersection.</summary>
internal sealed class ReportSet {
    public ReportSet(IEnumerable<ReportViewModel> reports) {
        Reports = (reports ?? throw new ArgumentNullException(nameof(reports))).ToArray();
    }

    public IReadOnlyCollection<ReportViewModel> Reports { get; }

    public ReportSetsIntersectionResult Intersect(ReportSet other) {
        if(other is null) throw new ArgumentNullException(nameof(other));

        var names = new HashSet<string>(other.Reports.Select(r => r.Name), StringComparer.CurrentCultureIgnoreCase);
        var ownNames = new HashSet<string>(Reports.Select(r => r.Name), StringComparer.CurrentCultureIgnoreCase);
        return new ReportSetsIntersectionResult(
            new ReportSet(Reports.Where(r => names.Contains(r.Name))),
            new ReportSet(other.Reports.Where(r => ownNames.Contains(r.Name))),
            new ReportSet(Reports.Where(r => !names.Contains(r.Name))),
            new ReportSet(other.Reports.Where(r => !ownNames.Contains(r.Name))));
    }
}

internal sealed class ReportSetsIntersectionResult {
    public ReportSetsIntersectionResult(ReportSet leftInnerSet, ReportSet rightInnerSet, ReportSet leftOuterSet, ReportSet rightOuterSet) {
        LeftInnerSet = leftInnerSet;
        RightInnerSet = rightInnerSet;
        LeftOuterSet = leftOuterSet;
        RightOuterSet = rightOuterSet;
    }

    public ReportSet LeftInnerSet { get; }
    public ReportSet RightInnerSet { get; }
    public ReportSet LeftOuterSet { get; }
    public ReportSet RightOuterSet { get; }
    public bool HasIntersection => LeftInnerSet.Reports.Count > 0;

    public IEnumerable<ReportsMergePair> GetMergePairs(ILocalizationService localization) {
        foreach(var existing in LeftInnerSet.Reports) {
            var importing = RightInnerSet.Reports.First(r => string.Equals(r.Name, existing.Name, StringComparison.CurrentCultureIgnoreCase));
            yield return new ReportsMergePair(localization, existing, importing);
        }
    }
}

internal sealed class ReportsMergePair {
    public ReportsMergePair(ILocalizationService localization, ReportViewModel existing, ReportViewModel importing) {
        if(localization is null) throw new ArgumentNullException(nameof(localization));
        Existing = existing;
        Importing = importing;

        var importingClashes = importing.GetClashes().ToList();
        var matched = new List<(ClashViewModel Existing, ClashViewModel Importing)>();
        foreach(var existingClash in existing.GetClashes()) {
            var imported = importingClashes.FirstOrDefault(c => c.Equals(existingClash));
            if(imported is null) continue;
            matched.Add((existingClash, imported));
            importingClashes.Remove(imported);
        }

        ExistingOuterClashes = existing.GetClashes().Where(c => matched.All(p => !ReferenceEquals(p.Existing, c))).ToArray();
        ImportingOuterClashes = importingClashes;
        var pairs = matched.Select(p => new ClashMergePairViewModel(localization, p.Existing, p.Importing)).ToArray();
        IntersectionClashes = new IntersectionClashes(pairs);
    }

    public ReportViewModel Existing { get; }
    public ReportViewModel Importing { get; }
    public IReadOnlyCollection<ClashViewModel> ExistingOuterClashes { get; }
    public IReadOnlyCollection<ClashViewModel> ImportingOuterClashes { get; }
    public IntersectionClashes IntersectionClashes { get; }
}

internal sealed class IntersectionClashes {
    public IntersectionClashes(IEnumerable<ClashMergePairViewModel> pairs) {
        var all = pairs.ToArray();
        Unchanged = all.Where(IsUnchanged).ToArray();
        Conflicted = all.Where(p => !IsUnchanged(p)).ToArray();
        NonConflicted = Array.Empty<ClashMergePairViewModel>();
    }

    public IReadOnlyCollection<ClashMergePairViewModel> Unchanged { get; }
    public IReadOnlyCollection<ClashMergePairViewModel> Conflicted { get; }
    public IReadOnlyCollection<ClashMergePairViewModel> NonConflicted { get; }

    private static bool IsUnchanged(ClashMergePairViewModel pair) =>
        pair.Existing.ClashName == pair.Importing.ClashName
        && pair.Existing.ClashStatus == pair.Importing.ClashStatus
        && pair.Existing.Comments.SequenceEqual(pair.Importing.Comments, new ClashCommentContentComparer());
}

internal sealed class ClashCommentContentComparer : IEqualityComparer<ClashCommentViewModel> {
    public bool Equals(ClashCommentViewModel x, ClashCommentViewModel y) =>
        ReferenceEquals(x, y) || x is not null && y is not null
        && x.Author == y.Author && x.Body == y.Body && x.Date == y.Date;

    public int GetHashCode(ClashCommentViewModel obj) {
        unchecked {
            int hash = 17;
            hash = hash * 31 + (obj.Author?.GetHashCode() ?? 0);
            hash = hash * 31 + (obj.Body?.GetHashCode() ?? 0);
            return hash * 31 + obj.Date.GetHashCode();
        }
    }
}

internal sealed class ClashIdDocComparer : IEqualityComparer<ClashViewModel> {
    public bool Equals(ClashViewModel x, ClashViewModel y) => x?.Equals(y) ?? y is null;
    public int GetHashCode(ClashViewModel obj) => obj?.GetHashCode() ?? 0;
}

internal sealed class ReportsIntersectionResolver {
    public IEnumerable<ReportViewModel> Replace(ReportSetsIntersectionResult intersection) =>
        intersection.LeftOuterSet.Reports.Union(intersection.RightInnerSet.Reports);

    public IEnumerable<ReportViewModel> CopyAndRename(ReportSetsIntersectionResult intersection) =>
        intersection.LeftOuterSet.Reports.Union(intersection.LeftInnerSet.Reports).Union(intersection.RightInnerSet.Reports);
}
