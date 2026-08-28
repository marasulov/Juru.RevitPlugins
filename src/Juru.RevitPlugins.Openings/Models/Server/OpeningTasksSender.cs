using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Core.FileNaming;
using Juru.RevitPlugins.Openings.Models.Configs;
using Juru.RevitPlugins.Openings.Models.Server.Dto;

namespace Juru.RevitPlugins.Openings.Models.Server;

/// <summary>
/// Отправляет задания активного файла на сервер согласования - см. <c>docs/OpeningsApi.md</c>.
/// </summary>
/// <remarks>
/// Отдельно от команды ленты, потому что отправка нужна из двух мест: с ленты и из навигатора
/// по выданным заданиям, где инженер тут же выбирает, с кем согласовывать. Диалоги остаются
/// вызывающему: сервис только делает работу и отдаёт готовый отчёт.
/// </remarks>
public class OpeningTasksSender {
    private readonly Document _doc;
    private readonly OpeningsConfig _config;
    private readonly OpeningsServerClient _client;

    public OpeningTasksSender(Document doc, OpeningsConfig config, OpeningsServerClient client) {
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>
    /// В файле не настроены общие координаты: задания уйдут относительно базовой точки проекта
    /// и в файле АР/КР могут встать со смещением. Спрашивать об этом - дело вызывающего.
    /// </summary>
    public bool NeedsCoordinateWarning => !ProjectCoordinatesCheck.HasAcquiredCoordinates(_doc);

    /// <summary>
    /// Сверяет ключи, сохраняет файл и отправляет все задания одной пачкой.
    /// </summary>
    /// <exception cref="InvalidOperationException">Сервер ответил ошибкой.</exception>
    /// <exception cref="TimeoutException">Сервер не ответил.</exception>
    /// <remarks>
    /// Меняет модель (ключи заданий) и сохраняет файл, поэтому вызывается только в контексте
    /// Revit API и вне открытой транзакции.
    /// </remarks>
    public OpeningSendOutcome Send(RevitFileNameInfo fileInfo) {
        if(fileInfo is null) {
            throw new ArgumentNullException(nameof(fileInfo));
        }

        var approvers = _config.GetApproverDisciplines();
        if(approvers.Count == 0) {
            return OpeningSendOutcome.Refused(
                "Не выбрано, с кем согласовывать: отметьте АР, КР или обе дисциплины. "
                + "Пачку без согласующих сервер принять не сможет - согласовывать её было бы некому.");
        }

        var adoption = AdoptTaskKeys(fileInfo, out string adoptionNote);
        SaveOrSynchronize();

        var batch = new OpeningBatchBuilder(_doc, _config).Build(fileInfo, "all", out var skipped);
        if(batch.Items.Count == 0) {
            return OpeningSendOutcome.Refused("В файле нет заданий стандартного вида - отправлять нечего.");
        }

        var submitted = Task.Run(() => _client.SubmitOpeningsAsync(batch)).GetAwaiter().GetResult();
        return new OpeningSendOutcome {
            Sent = batch.Items.Count,
            Report = BuildReport(batch, skipped.Count, submitted, adoption, adoptionNote)
        };
    }

    /// <summary>
    /// Возвращает пересозданным заданиям ключи, под которыми их места уже известны серверу -
    /// см. <see cref="TaskKeyReconciler"/>. Делается до сохранения: записанные ключи должны попасть
    /// в тот же файл, который увидит согласующий.
    /// </summary>
    /// <remarks>
    /// Недоступный или не умеющий отдавать задания сервер отправку не отменяет: задания уйдут
    /// с новыми ключами, как уходили раньше, а причина попадёт в отчёт. Останавливаться тут значило бы
    /// потерять и отправку тоже, хотя <c>POST</c> может работать и при неработающем <c>GET</c>.
    /// </remarks>
    private TaskKeyAdoptionResult? AdoptTaskKeys(RevitFileNameInfo fileInfo, out string note) {
        note = string.Empty;

        OpeningTaskViewDto[] serverTasks;
        try {
            serverTasks = Task.Run(() => _client.GetOpeningsAsync(
                    fileInfo.ProjectCode, fileInfo.ObjectName, [fileInfo.Discipline]))
                .GetAwaiter()
                .GetResult();
        } catch(Exception exception) when(exception is InvalidOperationException or TimeoutException) {
            note = "Ключи ранее выданных заданий не сверены (" + exception.Message
                + "): пересозданные задания уйдут как новые.";
            return null;
        }

        using var transaction = new Transaction(_doc, "Ключи заданий на отверстия");
        transaction.Start();

        var adoption = new TaskKeyReconciler(_doc, _config).Adopt(serverTasks);
        if(adoption.Adopted > 0) {
            transaction.Commit();
        } else {
            transaction.RollBack();
        }

        return adoption;
    }

    /// <summary>
    /// Сохраняет файл перед отправкой - из несохранённого плагин отправлять не даёт, иначе сервер
    /// знал бы задания, которых нет в файле, который увидит архитектор.
    /// </summary>
    private void SaveOrSynchronize() {
        if(!_doc.IsModified) {
            return;
        }

        if(_doc.IsWorkshared) {
            _doc.SynchronizeWithCentral(new TransactWithCentralOptions(), new SynchronizeWithCentralOptions());
        } else {
            _doc.Save();
        }
    }

    private string BuildReport(
        OpeningBatchDto batch,
        int skippedCount,
        OpeningSubmitResultDto result,
        TaskKeyAdoptionResult? adoption,
        string adoptionNote) {

        var report = new StringBuilder();
        report.AppendLine($"Отправлено: {batch.Items.Count}");
        report.AppendLine($"Согласуют: {string.Join(", ", batch.Approvers)}");
        report.AppendLine($"Принято новых: {result.Accepted}, обновлено: {result.Updated}");

        if(adoption is not null && adoption.Adopted > 0) {
            report.AppendLine($"Опознано как ранее выданные по месту: {adoption.Adopted}");
        }

        if(adoption is not null && adoption.ServerTasks == 0) {
            report.AppendLine("Сервер не знает ни одного задания по этому объекту, поэтому ключи "
                + "не сверялись: все задания уйдут как новые. Если задания отправлялись раньше - "
                + "это вопрос к серверу, а не к модели.");
        }

        if(adoption is not null && adoption.Ambiguous > 0) {
            report.AppendLine($"Не опознаны - рядом несколько подходящих: {adoption.Ambiguous}. "
                + "Они ушли как новые задания, согласование по ним нужно заново.");
        }

        if(adoption is not null && adoption.MissingParameter > 0) {
            report.AppendLine($"В {adoption.MissingParameter} заданиях нет параметра ключа "
                + "- семейство устарело, перезагрузите его, иначе задания будут терять согласование "
                + "при каждом пересоздании.");
        }

        if(adoption is not null && adoption.NotWritable > 0) {
            report.AppendLine($"В {adoption.NotWritable} заданиях параметр ключа есть, но записать "
                + "в него нельзя: в семействе он только для чтения (задан формулой или заведён "
                + "как параметр типа). Пока это не поправят в семействе, согласование будет "
                + "теряться при каждом пересоздании задания.");
        }

        if(adoptionNote.Length > 0) {
            report.AppendLine(adoptionNote);
        }

        if(skippedCount > 0) {
            report.AppendLine($"Пропущено нестандартных заданий (не описаны контрактом сервера): {skippedCount}");
        }

        if(result.Failed.Count > 0) {
            report.AppendLine();
            report.AppendLine($"Не отправлено: {result.Failed.Count}");
            foreach(var failure in result.Failed) {
                report.AppendLine($"  {failure.TaskKey}: {failure.Message}");
            }
        }

        return report.ToString();
    }
}
