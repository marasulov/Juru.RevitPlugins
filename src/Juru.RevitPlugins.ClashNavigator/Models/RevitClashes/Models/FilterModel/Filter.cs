using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

using Newtonsoft.Json;

using Juru.RevitPlugins.ClashNavigator.Models.FilterGenerators;
using Juru.RevitPlugins.ClashNavigator.Models.Interfaces;

namespace Juru.RevitPlugins.ClashNavigator.Models.FilterModel;
internal class Filter {
    public Filter(RevitRepository revitRepository) {
        RevitRepository = revitRepository;
    }

    public string Name { get; set; }
    public Set Set { get; set; }

    [JsonIgnore]
    public RevitRepository RevitRepository { get; set; }
    public List<ElementId> CategoryIds { get; set; }

    /// <summary>
    /// Шаблоны имён файлов из условия Navisworks "Исходный файл", например <c>*ARC_*</c>.
    /// </summary>
    /// <remarks>
    /// В фильтр Ревита такое условие не переводится: набор применяется к тем файлам, которые
    /// выбраны в проверке. Шаблоны хранятся, чтобы проверки, созданные из тестов Navisworks,
    /// получили нужные файлы. Пусто - набор не привязан к файлам.
    /// </remarks>
    public List<string> SourceFilePatterns { get; set; }

    public IEnumerable<IFilterableValueProvider> GetProviders() {
        return Set.GetProviders().Distinct();
    }

    public ElementFilter GetRevitFilter(Document doc, RevitFilterGenerator generator) {
        Set.FilterGenerator = generator;
        Set.Generate(doc);
        return generator.Generate();
    }
}
