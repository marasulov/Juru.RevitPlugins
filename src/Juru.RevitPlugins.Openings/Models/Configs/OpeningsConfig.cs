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

    /// <summary>
    /// Наименьший габарит элемента по умолчанию в мм: мельче отверстия не выдают.
    /// </summary>
    private const double _defaultMinSizeMm = 50;
    private const string _pluginFolderName = "Openings";

    // Атрибут System.Text.Json: у базового класса стоит атрибут Newtonsoft, который этот
    // сериализатор не видит, и служебные поля попадали в файл настроек.
    [System.Text.Json.Serialization.JsonIgnore]
    public override string ProjectConfigPath { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
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
    /// Имена вырезающих семейств чистовых отверстий и параметров внутри них.
    /// </summary>
    public RealOpeningFamilyNaming RealNaming { get; set; } = new RealOpeningFamilyNaming();

    /// <summary>
    /// Соответствия фрагмента имени файла и дисциплины - для файлов не по стандарту наименования.
    /// Применяются, только если имя не разобралось по стандарту.
    /// </summary>
    public List<DisciplineNameRule> DisciplineNameRules { get; set; } = GetDefaultDisciplineRules();

    /// <summary>
    /// Наименьшая глубина прохода в мм, при которой выдаётся задание. Ноль - выдавать и на касаниях.
    /// </summary>
    /// <remarks>
    /// Отсекает элементы, задевшие конструкцию углом. Bim4Everyone использует порог, равный
    /// примерно 15 мм, и на проходах через тонкие перегородки это даёт расхождение: задание там,
    /// где у нас отказ.
    /// </remarks>
    public double MinPassDepthMm { get; set; } = 15;

    /// <summary>
    /// Округление габаритов объединённого задания в мм.
    /// </summary>
    public int UnitedTaskSizeRoundingMm { get; set; } = 10;

    /// <summary>
    /// Наибольший зазор по толщине конструкции, при котором отверстия склеиваются в одно, мм.
    /// Слои, начерченные отдельными стенами, идут вплотную либо с воздушной прослойкой.
    /// </summary>
    public int UnitedTaskMaxGapMm { get; set; } = 50;

    /// <summary>
    /// Допустимый зазор между отверстиями вдоль конструкции и по вертикали, мм.
    /// </summary>
    /// <remarks>
    /// Здесь нужно почти нулевое значение: склейка транзитивна, и при допуске в несколько сантиметров
    /// цепочка соседних отверстий сливается в одно большое. Bim4Everyone склеивает только касающиеся
    /// солиды, то есть с нулевым зазором.
    /// </remarks>
    public int UnitedTaskTouchToleranceMm { get; set; } = 1;

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
    /// На каком расстоянии задание считается тем же, что уже известно серверу, при отправке -
    /// см. <c>TaskKeyReconciler</c>. Пересозданное задание попадает не совсем в ту же точку
    /// (округление отметок, чуть иная трасса), поэтому допуск не нулевой; но чем он больше,
    /// тем чаще рядом стоящие задания становятся неоднозначной парой и уходят как новые.
    /// </summary>
    public int TaskKeyMatchToleranceMm { get; set; } = 50;

    /// <summary>
    /// С кем согласовывать выдаваемые задания. Решает инженер: на объекте, где КР не работает
    /// в Revit, согласование с КР не состоится никогда, и ждать его - значит не прорезать
    /// ни одного отверстия. Хранится в настройках, чтобы выбор не повторяли при каждой отправке.
    /// </summary>
    public List<Discipline> Approvers { get; set; } = [Discipline.ARC, Discipline.STR];

    /// <summary>
    /// Согласующие дисциплины без мусора: только АР и КР, без повторов. Задания принимают
    /// только эти две дисциплины, а в файле настроек может оказаться что угодно.
    /// </summary>
    public IReadOnlyCollection<Discipline> GetApproverDisciplines() {
        return (Approvers ?? [])
            .Where(discipline => discipline is Discipline.ARC or Discipline.STR)
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Папка с эталонными семействами заданий на отверстия - оттуда плагин догружает семейство,
    /// если его вовсе нет в проекте при постановке заданий, полученных с сервера, в файле АР или КР.
    /// Если семейство в проекте уже есть - плагин его не трогает, даже устаревшую версию: не
    /// перезаписывает типы без явного действия пользователя.
    /// </summary>
    public string FamilyLibraryPath { get; set; } =
        @"G:\Shared drives\8. Knowledge & tools\21. Revit for E&IE\Families\00 Common\OpenngsFamilies";

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
                config.RealNaming ??= new RealOpeningFamilyNaming();
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
        // Значения по умолчанию повторяют настройки Bim4Everyone, на которых мы сверяли расстановку:
        // зазор 50 мм с каждой стороны, округление габаритов 50 мм и отметок 10 мм, форма
        // прямоугольная во всех диапазонах. Отличие одно: наименьший габарит 50 мм, а не ноль -
        // отверстия под трассы мельче не выдают, и гонять их через поиск незачем.
        return Enum.GetValues(typeof(MepKind))
            .Cast<MepKind>()
            .Select(kind => new MepCategorySettings {
                Kind = kind,
                MinDiameterMm = kind.IsRound() ? _defaultMinSizeMm : 0,
                MinWidthMm = kind.IsRound() ? 0 : _defaultMinSizeMm,
                MinHeightMm = kind.IsRound() ? 0 : _defaultMinSizeMm,
                Offsets = [
                    new OffsetRange {
                        FromMm = 0,
                        ToMm = 10000,
                        OffsetMm = 50,
                        Shape = OpeningShape.Rectangle
                    }
                ]
            })
            .ToList();
    }
}
