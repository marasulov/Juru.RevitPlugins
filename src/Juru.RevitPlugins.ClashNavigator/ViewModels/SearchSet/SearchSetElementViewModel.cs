using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

using dosymep.Revit;

using Juru.RevitPlugins.ClashNavigator.Models.Clashes;
using Juru.RevitPlugins.ClashNavigator.Models.Interfaces;

namespace Juru.RevitPlugins.ClashNavigator.ViewModels.SearchSet;
/// <summary>
/// Строка таблицы просмотра поискового набора: элемент и значения параметров из правил набора.
/// </summary>
internal class SearchSetElementViewModel {
    /// <param name="model">Элемент с документом и трансформацией связи.</param>
    /// <param name="element">Тот же элемент из Ревита - из него читаются значения параметров.</param>
    /// <param name="providers">Параметры правил набора, в порядке колонок таблицы.</param>
    public SearchSetElementViewModel(ElementModel model, Element element, IReadOnlyList<IFilterableValueProvider> providers) {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        if(providers is null) { throw new ArgumentNullException(nameof(providers)); }

        Id = Convert.ToInt64(model.Id.GetIdValue());
        Values = [.. providers.Select(provider => GetValue(provider, element))];
    }

    public ElementModel Model { get; }

    public string File => Model.DocumentName;

    public long Id { get; }

    public string Category => Model.Category;

    public string FamilyName => Model.FamilyName;

    public string Name => Model.Name;

    /// <summary>
    /// Значения параметров правил; колонки таблицы привязаны к ним по индексу.
    /// </summary>
    public string[] Values { get; }

    private static string GetValue(IFilterableValueProvider provider, Element element) {
        if(element is null) {
            return null;
        }
        try {
            return provider.GetElementParamValue(element)?.DisplayValue;
        } catch(Autodesk.Revit.Exceptions.ApplicationException) {
            // Значение для таблицы - справка, а не смысл окна: нечитаемый параметр не должен
            // ронять просмотр всего набора.
            return null;
        }
    }
}
