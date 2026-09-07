using dosymep.SimpleServices;

using Juru.RevitPlugins.ClashNavigator.Models.Evaluators;

namespace Juru.RevitPlugins.ClashNavigator.ViewModels.SearchSet;
/// <summary>
/// Переводит подписи условий правил на язык интерфейса.
/// </summary>
/// <remarks>
/// Подпись лежит в <c>RuleEvaluator.Message</c> / <c>SetEvaluator.Message</c> - в модели, которую
/// сериализуют в файл набора. Менять её на ключ нельзя: старые файлы придут с текстом, новые с
/// ключом, и в списке будет вперемешку. Поэтому подпись переписывается здесь, у уже созданного
/// объекта, а сопоставление при загрузке набора идёт по перечислению, а не по тексту - подпись
/// в файле ни на что не влияет.
/// </remarks>
internal static class EvaluatorNames {
    public static void Localize(ILocalizationService localizationService, RuleEvaluator evaluator) {
        string key = GetKey(evaluator.Evaluator);
        evaluator.Message = localizationService.GetLocalizedString(key) ?? evaluator.Message;
    }

    public static void Localize(ILocalizationService localizationService, SetEvaluator evaluator) {
        string key = evaluator.Evaluator == SetEvaluators.And ? "SetEvaluator.And" : "SetEvaluator.Or";
        evaluator.Message = localizationService.GetLocalizedString(key) ?? evaluator.Message;
    }

    private static string GetKey(RuleEvaluators evaluator) {
        return evaluator switch {
            RuleEvaluators.FilterStringEquals or RuleEvaluators.FilterNumericEquals => "Evaluator.Equals",
            RuleEvaluators.FilterNotEquals => "Evaluator.NotEquals",
            RuleEvaluators.FilterStringGreater or RuleEvaluators.FilterNumericGreater => "Evaluator.Greater",
            RuleEvaluators.FilterStringGreaterOrEqual or RuleEvaluators.FilterNumericGreaterOrEqual
                => "Evaluator.GreaterOrEqual",
            RuleEvaluators.FilterStringLess or RuleEvaluators.FilterNumericLess => "Evaluator.Less",
            RuleEvaluators.FilterStringLessOrEqual or RuleEvaluators.FilterNumericLessOrEqual
                => "Evaluator.LessOrEqual",
            RuleEvaluators.FilterStringContains => "Evaluator.Contains",
            RuleEvaluators.FilterStringNotContains => "Evaluator.NotContains",
            RuleEvaluators.FilterStringBeginsWith => "Evaluator.BeginsWith",
            RuleEvaluators.FilterStringNotBeginsWith => "Evaluator.NotBeginsWith",
            RuleEvaluators.FilterStringEndsWith => "Evaluator.EndsWith",
            RuleEvaluators.FilterStringNotEndsWith => "Evaluator.NotEndsWith",
            RuleEvaluators.FilterHasValue => "Evaluator.HasValue",
            _ => "Evaluator.HasNoValue"
        };
    }
}
