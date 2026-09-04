using System.Collections.Generic;

namespace Juru.RevitPlugins.Openings.Models.Configs;

/// <summary>
/// Что пользователь настроил под себя в окне согласования: какие колонки скрыты и какие решения
/// показывать.
/// </summary>
/// <remarks>
/// Отдельный файл в профиле пользователя, а не поле в <see cref="OpeningsConfig"/>: тот раскатывает
/// BIM-менеджер на всех, а нужные колонки и фильтр - дело того, кто согласовывает. Чтение и запись
/// файла - в <see cref="NavigatorLayoutStore"/>, общем с навигатором выданных заданий.
/// </remarks>
public class IncomingOpeningsNavigatorLayout {
    private const string _fileName = nameof(IncomingOpeningsNavigatorLayout) + ".json";

    /// <summary>
    /// Заголовки скрытых колонок. Хранятся заголовками, а не номерами: номер съезжает, как только
    /// в гриде появляется новая колонка, и пользователь получал бы скрытой не ту, что убрал.
    /// </summary>
    public List<string> HiddenColumns { get; set; } = [];

    public bool ShowPending { get; set; } = true;

    public bool ShowApproved { get; set; } = true;

    public bool ShowRejected { get; set; } = true;

    public static IncomingOpeningsNavigatorLayout Load() {
        return NavigatorLayoutStore.Load<IncomingOpeningsNavigatorLayout>(_fileName);
    }

    public void Save() {
        NavigatorLayoutStore.Save(_fileName, this);
    }
}
