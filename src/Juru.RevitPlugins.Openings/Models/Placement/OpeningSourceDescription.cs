using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

namespace Juru.RevitPlugins.Openings.Models.Placement;

/// <summary>
/// Формат текста в <c>Juru_Opening_Description</c>: файлы, имена и id элементов, из пересечения
/// которых возникло задание. Запись и разбор живут рядом - разбор нужен, чтобы опознать в уже
/// стоящем задании тот же инженерный элемент, что и в новом рассчитанном отверстии (см.
/// <see cref="PlacedTasksIndex"/>), а форматы должны совпадать.
/// </summary>
public static class OpeningSourceDescription {
    /// <summary>
    /// Возвращает описание задания: файлы, имена и id элементов, из пересечения которых оно возникло.
    /// У объединённого задания перечисляются все, иначе после склейки не найти, откуда оно взялось.
    /// </summary>
    public static string Write(IEnumerable<(Element Mep, Element Structure)> sources) {
        return string.Join("; ", sources.Select(source =>
            $"{source.Mep.Document.Title}: {source.Mep.Name}: {source.Mep.Id}"
            + $"; {source.Structure.Document.Title}: {source.Structure.Name}: {source.Structure.Id}"));
    }

    /// <summary>
    /// Возвращает id инженерных элементов, записанных в описание - каждый второй кусок между "; ",
    /// начиная с первого (структура идёт следом за своим инженерным элементом). Возвращает пусто,
    /// если распознать не удалось: старое описание другого формата не должно валить сверку,
    /// худший случай - задание не опознано как то же самое и получит дубль, как и раньше.
    /// </summary>
    public static IReadOnlyCollection<int> ParseMepElementIds(string description) {
        if(string.IsNullOrWhiteSpace(description)) {
            return [];
        }

        var chunks = description.Split(["; "], StringSplitOptions.RemoveEmptyEntries);
        var ids = new List<int>();
        for(int i = 0; i < chunks.Length; i += 2) {
            var parts = chunks[i].Split([": "], StringSplitOptions.None);
            if(parts.Length > 0 && int.TryParse(parts[^1].Trim(), out int id)) {
                ids.Add(id);
            }
        }

        return ids;
    }
}
