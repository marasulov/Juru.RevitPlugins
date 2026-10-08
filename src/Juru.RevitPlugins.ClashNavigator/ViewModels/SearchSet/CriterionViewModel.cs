using dosymep.WPF.ViewModels;

using Juru.RevitPlugins.ClashNavigator.Models.FilterModel;

namespace Juru.RevitPlugins.ClashNavigator.ViewModels.SearchSet;
/// <summary>
/// Узел дерева критериев поискового набора: либо правило, либо вложенный набор.
/// </summary>
internal abstract class CriterionViewModel : BaseViewModel {
    /// <summary>
    /// Набор, в котором лежит узел. У корневого набора - null.
    /// </summary>
    public SetViewModel Parent { get; set; }

    /// <summary>
    /// Корневой набор удалить нельзя: он и есть сам поисковый набор.
    /// </summary>
    public bool CanRemove => Parent != null;

    /// <summary>
    /// Отличает набор от правила. Разметка выбирает шаблон узла по этому признаку, а не по типу:
    /// модели представления внутренние, и ссылаться на них из XAML нельзя.
    /// </summary>
    public abstract bool IsSet { get; }

    /// <summary>
    /// Собирает критерий модели по тому, что настроено в узле.
    /// </summary>
    public abstract Criterion GetCriterion();

    /// <summary>
    /// Возвращает описание того, чего не хватает узлу для сохранения, или null, если узел заполнен.
    /// </summary>
    public abstract string GetErrorText();

    /// <summary>
    /// Строка, по которой видно, менялся ли узел: окно сравнивает её с той, что была при
    /// последнем сохранении, и спрашивает о сохранении при закрытии только при отличии.
    /// </summary>
    public abstract string GetSignature();
}
