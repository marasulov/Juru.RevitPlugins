using System.Collections.Generic;

namespace Juru.RevitPlugins.Openings.Models.Configs;

/// <summary>
/// Что инженер настроил под себя в навигаторе по выданным заданиям: какие колонки скрыты.
/// </summary>
/// <remarks>
/// Фильтра решений здесь нет, в отличие от окна согласования: решения по своим заданиям инженер
/// не принимает, и прятать их незачем - ему нужно видеть, кто ещё не ответил.
/// </remarks>
public class OpeningsNavigatorLayout {
    private const string _fileName = nameof(OpeningsNavigatorLayout) + ".json";

    /// <summary>
    /// Заголовки скрытых колонок. Хранятся заголовками, а не номерами: номер съезжает, как только
    /// в гриде появляется новая колонка, и пользователь получал бы скрытой не ту, что убрал.
    /// </summary>
    public List<string> HiddenColumns { get; set; } = [];

    public static OpeningsNavigatorLayout Load() {
        return NavigatorLayoutStore.Load<OpeningsNavigatorLayout>(_fileName);
    }

    public void Save() {
        NavigatorLayoutStore.Save(_fileName, this);
    }
}
