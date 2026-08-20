using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

using Juru.RevitPlugins.Core;
using Juru.RevitPlugins.Core.FileNaming;
using Juru.RevitPlugins.Core.Profiles;
using Juru.RevitPlugins.Core.ProjectConfigs;

namespace Juru.RevitPlugins.Openings.Models.Configs;

/// <summary>
/// Настройки выдачи заданий на отверстия и размещения чистовых отверстий.
/// </summary>
public class OpeningsConfig : ProjectConfig {
    private const string _configFileName = nameof(OpeningsConfig) + ".json";
    private const string _pluginFolderName = "Openings";

    [JsonIgnore]
    public override string ProjectConfigPath { get; set; }

    [JsonIgnore]
    public override IConfigSerializer Serializer { get; set; }

    /// <summary>
    /// Название набора настроек, чтобы отличать проектные настройки от настроек по умолчанию.
    /// </summary>
    public string Name { get; set; } = "по умолчанию";

    /// <summary>
    /// Настройки на каждый вид инженерных элементов.
    /// </summary>
    public List<MepCategorySettings> Categories { get; set; } = GetDefaultCategories();

    /// <summary>
    /// Имена семейств заданий и параметров внутри них.
    /// </summary>
    public OpeningFamilyNaming Naming { get; set; } = new OpeningFamilyNaming();

    /// <summary>
    /// Соответствия фрагмента имени файла и дисциплины - для файлов не по стандарту наименования.
    /// Применяются, только если имя не разобралось по стандарту.
    /// </summary>
    public List<DisciplineNameRule> DisciplineNameRules { get; set; } = GetDefaultDisciplineRules();

    /// <summary>
    /// Округление габаритов объединённого задания в мм.
    /// </summary>
    public int UnitedTaskSizeRoundingMm { get; set; } = 10;

    /// <summary>
    /// Наибольший зазор между проходами вдоль элемента, при котором они склеиваются в одно задание, мм.
    /// Слои конструкции, начерченные отдельными стенами, идут вплотную либо с воздушной прослойкой.
    /// </summary>
    public int UnitedTaskMaxGapMm { get; set; } = 150;

    /// <summary>
    /// Допустимое смещение проходов поперёк оси элемента при склейке, мм.
    /// Больше этого - это уже разные трассы, а не слои одной конструкции.
    /// </summary>
    public int UnitedTaskLateralToleranceMm { get; set; } = 50;

    /// <summary>
    /// Показывать ли отчёт о пересечениях, для которых задание разместить не удалось.
    /// </summary>
    public bool ShowPlacingErrors { get; set; } = true;

    /// <summary>
    /// Адрес сервера, на который уходят выданные задания и решения по согласованию.
    /// Пустое значение означает адрес по умолчанию.
    /// </summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>
    /// Округление габаритов чистового отверстия в мм.
    /// </summary>
    public int RealOpeningSizeRoundingMm { get; set; } = 10;

    /// <summary>
    /// Округление отметки чистового отверстия в мм.
    /// </summary>
    public int RealOpeningElevationRoundingMm { get; set; } = 1;

    /// <summary>
    /// Читает настройки: сначала настройки пользователя, затем общие настройки института,
    /// иначе настройки по умолчанию.
    /// </summary>
    public static OpeningsConfig GetOpeningsConfig() {
        var serializer = new JsonConfigSerializer();
        string localPath = GetConfigPath(ProfilePaths.Local);

        var config = Read(localPath, serializer);
        if(config is null && ProfilePaths.SharedAvailable) {
            config = Read(GetConfigPath(ProfilePaths.Shared), serializer);
        }

        config ??= new OpeningsConfig();

        // Пишем всегда в профиль пользователя: общие настройки принадлежат BIM-менеджеру.
        config.ProjectConfigPath = localPath;
        config.Serializer = serializer;
        return config;
    }

    /// <summary>
    /// Возвращает настройки для вида инженерных элементов, либо <c>null</c>, если вид в настройках отсутствует.
    /// </summary>
    public MepCategorySettings GetCategory(MepKind kind) {
        return Categories.FirstOrDefault(category => category.Kind == kind);
    }

    /// <summary>
    /// Возвращает виды инженерных элементов, выбранные для поиска пересечений.
    /// </summary>
    public IReadOnlyCollection<MepCategorySettings> GetSelectedCategories() {
        return Categories.Where(category => category.IsSelected).ToArray();
    }

    private static OpeningsConfig Read(string path, IConfigSerializer serializer) {
        if(!File.Exists(path)) {
            return null;
        }

        try {
            var config = serializer.Deserialize<OpeningsConfig>(File.ReadAllText(path));
            // Настройки, сохранённые предыдущей версией плагина, могут не содержать список категорий.
            if(config is not null && (config.Categories is null || config.Categories.Count == 0)) {
                config.Categories = GetDefaultCategories();
            }
            if(config is not null) {
                config.Naming ??= new OpeningFamilyNaming();
                if(config.DisciplineNameRules is null || config.DisciplineNameRules.Count == 0) {
                    config.DisciplineNameRules = GetDefaultDisciplineRules();
                }
            }
            return config;
        } catch(Exception) {
            // Нечитаемый файл настроек не должен мешать работе: берём настройки по умолчанию.
            return null;
        }
    }

    private static string GetConfigPath(string profilePath) {
        return Path.Combine(profilePath, RevitVersionInfo.Version, _pluginFolderName, _configFileName);
    }

    private static List<DisciplineNameRule> GetDefaultDisciplineRules() {
        return [
            new DisciplineNameRule { Fragment = "_OV", Discipline = Discipline.HVC },
            new DisciplineNameRule { Fragment = "_ОВ", Discipline = Discipline.HVC },
            new DisciplineNameRule { Fragment = "_HVAC", Discipline = Discipline.HVC },
            new DisciplineNameRule { Fragment = "_VK", Discipline = Discipline.WSS },
            new DisciplineNameRule { Fragment = "_ВК", Discipline = Discipline.WSS },
            new DisciplineNameRule { Fragment = "_EL", Discipline = Discipline.ELE },
            new DisciplineNameRule { Fragment = "_ЭОМ", Discipline = Discipline.ELE },
            new DisciplineNameRule { Fragment = "_AR", Discipline = Discipline.ARC },
            new DisciplineNameRule { Fragment = "_АР", Discipline = Discipline.ARC },
            new DisciplineNameRule { Fragment = "_KR", Discipline = Discipline.STR },
            new DisciplineNameRule { Fragment = "_КР", Discipline = Discipline.STR }
        ];
    }

    private static List<MepCategorySettings> GetDefaultCategories() {
        return [
            new MepCategorySettings {
                Kind = MepKind.Pipe,
                MinSizeMm = 50,
                Offsets = [
                    new OffsetRange { FromMm = 0, ToMm = 300, OffsetMm = 50, Shape = OpeningShape.Round },
                    new OffsetRange { FromMm = 300, ToMm = 10000, OffsetMm = 50, Shape = OpeningShape.Rectangle }
                ]
            },
            new MepCategorySettings {
                Kind = MepKind.RoundDuct,
                MinSizeMm = 100,
                Offsets = [
                    new OffsetRange { FromMm = 0, ToMm = 300, OffsetMm = 50, Shape = OpeningShape.Round },
                    new OffsetRange { FromMm = 300, ToMm = 10000, OffsetMm = 50, Shape = OpeningShape.Rectangle }
                ]
            },
            new MepCategorySettings {
                Kind = MepKind.RectangleDuct,
                MinSizeMm = 100,
                Offsets = [
                    new OffsetRange { FromMm = 0, ToMm = 10000, OffsetMm = 50, Shape = OpeningShape.Rectangle }
                ]
            },
            new MepCategorySettings {
                Kind = MepKind.CableTray,
                MinSizeMm = 100,
                Offsets = [
                    new OffsetRange { FromMm = 0, ToMm = 10000, OffsetMm = 50, Shape = OpeningShape.Rectangle }
                ]
            },
            new MepCategorySettings {
                Kind = MepKind.Conduit,
                IsSelected = false,
                MinSizeMm = 50,
                Offsets = [
                    new OffsetRange { FromMm = 0, ToMm = 10000, OffsetMm = 50, Shape = OpeningShape.Round }
                ]
            }
        ];
    }
}
