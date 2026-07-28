using Juru.RevitPlugins.ClashNavigator.Models.FilterModel;

namespace Juru.RevitPlugins.ClashNavigator.ViewModels.FilterCreatorViewModels.Interfaces;
internal interface IСriterionViewModel {
    void Renew();
    bool IsEmpty();
    string GetErrorText();
    void Initialize();
    Criterion GetCriterion();
}
