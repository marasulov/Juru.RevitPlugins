using System.Collections.Generic;
using System.Linq;

using Juru.RevitPlugins.ClashNavigator.Models.Clashes;
using Juru.RevitPlugins.ClashNavigator.Models.Interfaces;

namespace Juru.RevitPlugins.ClashNavigator.Models.ClashDetection;
internal class ClashDetector {
    public const string DefaultNamePrefix = "Конфликт";
    private readonly RevitRepository _revitRepository;
    private readonly IEnumerable<IProvider> _mainDocProviders;
    private readonly IEnumerable<IProvider> _otherProviders;

    public ClashDetector(RevitRepository revitRepository, IEnumerable<IProvider> mainDocProviders, IEnumerable<IProvider> otherProviders) {
        _revitRepository = revitRepository;
        _mainDocProviders = mainDocProviders;
        _otherProviders = otherProviders;
    }

    public List<ClashModel> FindClashes() {
        List<ClashModel> clashes = [];
        foreach(var mainProvider in _mainDocProviders) {
            foreach(var provider in _otherProviders) {
                var providerClashDetector = new ProvidersClashDetector(_revitRepository, mainProvider, provider);
                clashes.AddRange(providerClashDetector.GetClashes());
            }
        }

        clashes = clashes.Distinct().ToList();
        for(int i = 0; i < clashes.Count; i++) {
            clashes[i].Name = $"{DefaultNamePrefix}{i + 1}";
        }

        return clashes;
    }
}

internal class ClashesMarker {
    /// <summary>
    /// Сколько коллизий этого прогона не встречалось в прошлом.
    /// </summary>
    /// <remarks>
    /// Сравнение идёт тем же <c>ClashModel.Equals</c>, что и перенос статусов ниже: коллизия
    /// считается той же самой, если совпала пара элементов - в любом порядке. Иначе "новых"
    /// оказалось бы больше, чем на самом деле, каждый раз, когда стороны поменялись местами.
    /// </remarks>
    public static int CountAdded(
        ICollection<ClashModel> newClashes,
        ICollection<ClashModel> oldClashes) {
        return newClashes.Count(item => !oldClashes.Any(old => old.Equals(item)));
    }

    /// <summary>
    /// Сколько коллизий прошлого прогона в этом не встретилось.
    /// </summary>
    public static int CountGone(
        ICollection<ClashModel> newClashes,
        ICollection<ClashModel> oldClashes) {
        return oldClashes.Count(old => !newClashes.Any(item => item.Equals(old)));
    }

    public static IEnumerable<ClashModel> MarkSolvedClashes(
        ICollection<ClashModel> newClashes,
        ICollection<ClashModel> oldClashes) {
        foreach(var newClash in newClashes) {
            var oldClash = oldClashes.FirstOrDefault(item => item.Equals(newClash));
            if(oldClash != null) {
                newClash.ClashStatus = oldClash.ClashStatus;
                newClash.Name = string.IsNullOrWhiteSpace(oldClash.Name) ? newClash.Name : oldClash.Name;
                newClash.Comments = oldClash.Comments;
            }
            yield return newClash;
        }
    }
}
