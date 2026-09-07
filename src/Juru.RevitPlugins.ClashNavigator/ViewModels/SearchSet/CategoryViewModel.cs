using System;

using Autodesk.Revit.DB;

using dosymep.WPF.ViewModels;

using Juru.RevitPlugins.ClashNavigator.Models;

namespace Juru.RevitPlugins.ClashNavigator.ViewModels.SearchSet;
/// <summary>
/// Категория модели в списке выбора категорий поискового набора.
/// </summary>
internal class CategoryViewModel : BaseViewModel, IName {
    public CategoryViewModel(Category category) {
        Category = category ?? throw new ArgumentNullException(nameof(category));
    }

    public Category Category { get; }

    public string Name => Category.Name;

    public ElementId Id => Category.Id;
}
