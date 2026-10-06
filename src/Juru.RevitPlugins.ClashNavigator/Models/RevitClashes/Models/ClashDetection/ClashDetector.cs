using System.Collections.Generic;
using System.Linq;

using Juru.RevitPlugins.ClashNavigator.Models.Clashes;
using Juru.RevitPlugins.ClashNavigator.Models.Interfaces;
using Juru.RevitPlugins.ClashNavigator.Services;

namespace Juru.RevitPlugins.ClashNavigator.Models.ClashDetection;
internal class ClashDetector {
    public const string DefaultNamePrefix = "Конфликт";
    private readonly RevitRepository _revitRepository;
    private readonly IEnumerable<IProvider> _mainDocProviders;
    private readonly IEnumerable<IProvider> _otherProviders;
    private readonly double _tolerance;

    /// <param name="tolerance">Допуск во внутренних единицах (футах): пересечения мельче не считаются.</param>
    public ClashDetector(
        RevitRepository revitRepository,
        IEnumerable<IProvider> mainDocProviders,
        IEnumerable<IProvider> otherProviders,
        double tolerance = 0) {
        _tolerance = tolerance;
        _revitRepository = revitRepository;
        _mainDocProviders = mainDocProviders;
        _otherProviders = otherProviders;
    }

    /// <param name="progress">Окно хода поиска, общее для всех проверок прогона.</param>
    /// <param name="checkName">Имя проверки - начало подписи этапа в окне хода поиска.</param>
    public List<ClashModel> FindClashes(ClashSearchProgress progress, string checkName) {
        List<ClashModel> clashes = [];
        foreach(var mainProvider in _mainDocProviders) {
            foreach(var provider in _otherProviders) {
                progress.Token.ThrowIfCancellationRequested();
                string stage = $"{checkName}: {RevitRepository.GetDocumentName(mainProvider.Doc)}"
                               + $" × {RevitRepository.GetDocumentName(provider.Doc)}";
                var providerClashDetector = new ProvidersClashDetector(_revitRepository, mainProvider, provider, _tolerance);
                clashes.AddRange(providerClashDetector.GetClashes(progress, stage));
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
