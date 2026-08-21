using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Openings.Models.Configs;

namespace Juru.RevitPlugins.Openings.Models.Navigator;

/// <summary>
/// Находит в активном файле задания на отверстия, выданные из него.
/// </summary>
/// <remarks>
/// Задания отбираются по именам семейств из настроек, а не по категории: в файле инженера в той же
/// категории обобщённых моделей лежит и оборудование, и закладные, и задания чужих плагинов.
/// </remarks>
public class OpeningTasksProvider {
    private readonly Document _doc;
    private readonly OpeningsConfig _config;

    public OpeningTasksProvider(Document doc, OpeningsConfig config) {
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// Возвращает все задания на отверстия активного файла, включая нестандартные,
    /// размещённые вручную отдельным семейством.
    /// </summary>
    public IReadOnlyCollection<OpeningTask> GetTasks() {
        var typesByFamily = GetOpeningTypesByFamilyName();
        List<OpeningTask> tasks = [];

        foreach(var instance in GetFamilyInstances()) {
            string? familyName = instance.Symbol?.FamilyName;
            if(familyName is null) {
                continue;
            }

            if(typesByFamily.TryGetValue(familyName, out var openingType)) {
                tasks.Add(new OpeningTask(instance, openingType, _config.Naming));
            } else if(familyName.Equals(_config.Naming.SlabUniqueFamily, StringComparison.CurrentCultureIgnoreCase)) {
                tasks.Add(new OpeningTask(instance, null, _config.Naming) {
                    Status = OpeningTaskStatus.Unique
                });
            }
        }

        return tasks;
    }

    private Dictionary<string, OpeningType> GetOpeningTypesByFamilyName() {
        var types = new Dictionary<string, OpeningType>(StringComparer.CurrentCultureIgnoreCase);
        foreach(var openingType in Enum.GetValues(typeof(OpeningType)).Cast<OpeningType>()) {
            types[_config.Naming.GetFamilyName(openingType)] = openingType;
        }
        return types;
    }

    private IEnumerable<FamilyInstance> GetFamilyInstances() {
        return new FilteredElementCollector(_doc)
            .OfClass(typeof(FamilyInstance))
            .WhereElementIsNotElementType()
            .Cast<FamilyInstance>();
    }
}
