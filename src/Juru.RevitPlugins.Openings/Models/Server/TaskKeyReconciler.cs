using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Openings.Models.Configs;
using Juru.RevitPlugins.Openings.Models.Navigator;
using Juru.RevitPlugins.Openings.Models.Server.Dto;

namespace Juru.RevitPlugins.Openings.Models.Server;

/// <summary>
/// Возвращает заново созданному заданию ключ, под которым его место уже известно серверу.
/// </summary>
/// <remarks>
/// Ключ задания (<c>taskKey</c> в <c>docs/OpeningsApi.md</c>) хранится в параметре экземпляра
/// <see cref="OpeningFamilyNaming.TaskKeyParam"/>, а <c>UniqueId</c> остаётся лишь значением
/// по умолчанию для задания, которое сервер ещё не видел. Без этого шага "удалил задания в модели
/// и расставил заново" уносило на сервер новые ключи: прежние задания помечались удалёнными вместе
/// со всей историей согласования, а их копии на тех же местах приходили как никем не смотренные.
/// <para>
/// Совпадение ищется только по месту и типу отверстия, габариты не сверяются: пересозданное задание
/// другого размера - это то же отверстие с изменившейся геометрией, и решение по нему сбрасывает
/// сервер, а не плагин. Пара принимается, когда она однозначна с двух сторон: у задания в файле ровно
/// один кандидат на сервере и у этого кандидата ровно одно задание в файле. Два задания ближе допуска
/// друг к другу оставляют оба ключа новыми - лучше лишнее согласование, чем чужая история решений,
/// приписанная не тому отверстию. Из нескольких записей в одном месте выбор делает
/// <see cref="Choose"/>.
/// </para>
/// </remarks>
public class TaskKeyReconciler {
    private readonly Document _doc;
    private readonly OpeningsConfig _config;

    public TaskKeyReconciler(Document doc, OpeningsConfig config) {
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// Записывает заданиям без своего ключа ключи совпавших по месту заданий сервера.
    /// </summary>
    /// <param name="serverTasks">Задания этой дисциплины, уже известные серверу, включая удалённые.</param>
    /// <remarks>
    /// Меняет модель, поэтому вызывается только внутри открытой транзакции.
    /// </remarks>
    public TaskKeyAdoptionResult Adopt(IReadOnlyCollection<OpeningTaskViewDto> serverTasks) {
        if(serverTasks is null) {
            throw new ArgumentNullException(nameof(serverTasks));
        }

        var result = new TaskKeyAdoptionResult { ServerTasks = serverTasks.Count };
        var tasks = new OpeningTasksProvider(_doc, _config).GetTasks();

        // Ключ, уже записанный в модели, занимает своего кандидата: иначе задание, стоящее рядом
        // со своим соседом, могло бы забрать чужой ключ у задания, которое его законно держит.
        var boundKeys = new HashSet<string>(
            tasks.Select(GetStoredKey).Where(key => key is not null)!,
            StringComparer.Ordinal);

        var unkeyed = tasks
            .Where(task => task.OpeningType is not null && GetStoredKey(task) is null)
            .ToList();
        var candidates = serverTasks
            .Where(serverTask => !boundKeys.Contains(serverTask.TaskKey))
            .ToList();

        if(unkeyed.Count == 0 || candidates.Count == 0) {
            return result;
        }

        var pairs = GetPairs(unkeyed, candidates);
        var candidatesByTask = pairs
            .GroupBy(pair => pair.Task)
            .ToDictionary(group => group.Key, group => group.Select(pair => pair.Candidate).ToList());

        var chosen = new Dictionary<OpeningTask, OpeningTaskViewDto>();
        foreach(var entry in candidatesByTask) {
            var candidate = Choose(entry.Key, entry.Value);
            if(candidate is not null) {
                chosen[entry.Key] = candidate;
            }
        }

        // Один кандидат - одно задание: если на запись сервера претендуют двое, ключ не получает
        // никто. Иначе история согласования досталась бы тому из них, кто просто раньше в списке.
        var takenTwice = chosen
            .GroupBy(entry => entry.Value)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group.Select(entry => entry.Key))
            .ToHashSet();

        foreach(var entry in chosen) {
            if(takenTwice.Contains(entry.Key)) {
                continue;
            }

            switch(SetKey(entry.Key, entry.Value.TaskKey)) {
                case KeyWriteResult.Written:
                    result.Adopted++;
                    break;
                case KeyWriteResult.NoParameter:
                    result.MissingParameter++;
                    break;
                default:
                    result.NotWritable++;
                    break;
            }
        }

        // Задание, у которого кандидаты были, но однозначной пары не вышло: уходит с новым ключом,
        // и это стоит показать - место, где согласование теряется не из-за правки трассы.
        result.Ambiguous = candidatesByTask.Keys
            .Count(task => !chosen.ContainsKey(task) || takenTwice.Contains(task));

        return result;
    }

    /// <summary>
    /// Выбирает из совпавших по месту записей сервера ту, которой задание и является.
    /// </summary>
    /// <remarks>
    /// Своя запись важнее любой другой: задание уже уходило на сервер под своим <c>UniqueId</c>,
    /// и сверка лишь закрепляет этот ключ в параметре. Без этого правила задание, отправленное
    /// до появления параметра, навсегда оставалось бы неоднозначным - в его месте лежат и его
    /// собственная запись, и удалённая запись задания, которое стояло там раньше.
    /// <para>
    /// Из чужих записей живая важнее удалённой: удалённая осталась от задания, которое в этом месте
    /// уже не существует, и живой кандидат - более точное описание того, что стоит здесь сейчас.
    /// Несколько живых кандидатов в допуске - отказ, тут угадывать нечего.
    /// </para>
    /// </remarks>
    private static OpeningTaskViewDto? Choose(OpeningTask task, List<OpeningTaskViewDto> candidates) {
        var own = candidates.FirstOrDefault(
            candidate => string.Equals(candidate.TaskKey, task.UniqueId, StringComparison.Ordinal));
        if(own is not null) {
            return own;
        }

        var alive = candidates.Where(candidate => !candidate.Deleted).ToList();
        if(alive.Count == 1) {
            return alive[0];
        }

        if(alive.Count > 1) {
            return null;
        }

        return candidates.Count == 1 ? candidates[0] : null;
    }

    private List<(OpeningTask Task, OpeningTaskViewDto Candidate)> GetPairs(
        IReadOnlyCollection<OpeningTask> unkeyed,
        IReadOnlyCollection<OpeningTaskViewDto> candidates) {

        double tolerance = UnitUtils.ConvertToInternalUnits(
            _config.TaskKeyMatchToleranceMm, UnitTypeId.Millimeters);
        var projectBasePoint = BasePoint.GetProjectBasePoint(_doc).Position;
        var pairs = new List<(OpeningTask Task, OpeningTaskViewDto Candidate)>();

        foreach(var task in unkeyed) {
            var location = task.Location;
            if(location is null) {
                continue;
            }

            foreach(var candidate in candidates) {
                if(candidate.OpeningType != task.OpeningType) {
                    continue;
                }

                var candidatePoint = ToInternal(candidate.Location) + projectBasePoint;
                if(location.DistanceTo(candidatePoint) <= tolerance) {
                    pairs.Add((task, candidate));
                }
            }
        }

        return pairs;
    }

    private string? GetStoredKey(OpeningTask task) {
        string? key = task.Instance.LookupParameter(_config.Naming.TaskKeyParam)?.AsString();
        return string.IsNullOrWhiteSpace(key) ? null : key;
    }

    private KeyWriteResult SetKey(OpeningTask task, string key) {
        var parameter = task.Instance.LookupParameter(_config.Naming.TaskKeyParam);
        if(parameter is null) {
            return KeyWriteResult.NoParameter;
        }

        if(parameter.IsReadOnly) {
            return KeyWriteResult.ReadOnly;
        }

        return parameter.Set(key) ? KeyWriteResult.Written : KeyWriteResult.Refused;
    }

    private enum KeyWriteResult {
        Written,
        NoParameter,
        ReadOnly,
        Refused
    }

    private static XYZ ToInternal(OpeningLocationDto location) {
        return new XYZ(
            UnitUtils.ConvertToInternalUnits(location.X, UnitTypeId.Meters),
            UnitUtils.ConvertToInternalUnits(location.Y, UnitTypeId.Meters),
            UnitUtils.ConvertToInternalUnits(location.Z, UnitTypeId.Meters));
    }
}
