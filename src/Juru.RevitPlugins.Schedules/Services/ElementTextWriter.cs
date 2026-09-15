using System;
using System.Collections.Generic;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Schedules.Localization;
using Juru.RevitPlugins.Schedules.Models;

namespace Juru.RevitPlugins.Schedules.Services;

/// <summary>
///     Пишет переводы в параметры элементов и откатывает их назад. Транзакцию открывает вызывающий:
///     весь прогон по категории должен либо примениться, либо не примениться целиком, иначе половина
///     модели окажется на английском без возможности назвать, какая.
/// </summary>
public sealed class ElementTextWriter(Document document)
{
    private readonly List<string> failures = [];

    private int written;
    private int reverted;
    private int unchanged;

    /// <summary>
    ///     Записывает перевод, запомнив русский исходник. Пустой перевод трактуется как откат: так строку
    ///     возвращают к русскому виду, просто вычистив клетку с переводом.
    /// </summary>
    public void Write(ElementText text, string translation)
    {
        if (text is null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        var trimmed = translation?.Trim() ?? string.Empty;

        if (trimmed.Length is 0)
        {
            Revert(text);
            return;
        }

        Apply(text, element =>
        {
            if (string.Equals(TextTargetAccess.Read(element, text.Target), trimmed, StringComparison.Ordinal))
            {
                unchanged++;
                return;
            }

            // Исходник запоминается до записи и только если запись удалась: иначе элемент оказался бы
            // помечен переведённым, оставшись русским, и откат вернул бы ему то же самое.
            SourceTextStore.Remember(element, text.Target, text.SourceText);

            if (TextTargetAccess.Write(element, text.Target, trimmed))
            {
                written++;
                return;
            }

            SourceTextStore.Forget(element, text.Target);

            failures.Add(string.Format(Text.FailureCannotWrite, text.Context, text.What));
        });
    }

    /// <summary>
    ///     Возвращает цель к русскому исходнику. Элемент, который не переводился, не трогается:
    ///     иначе откат затёр бы название, набранное в модели вручную.
    /// </summary>
    public void Revert(ElementText text)
    {
        if (text is null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        Apply(text, element =>
        {
            var stored = SourceTextStore.TryRead(element, text.Target);

            if (stored is null)
            {
                unchanged++;
                return;
            }

            if (!TextTargetAccess.Write(element, text.Target, stored))
            {
                failures.Add(string.Format(Text.FailureCannotRevert, text.Context, text.What));
                return;
            }

            SourceTextStore.Forget(element, text.Target);

            reverted++;
        });
    }

    public ElementTextApplyResult Describe(string category)
    {
        return new ElementTextApplyResult
        {
            Category = category,
            WrittenCount = written,
            RevertedCount = reverted,
            UnchangedCount = unchanged,
            Failures = failures
        };
    }

    /// <summary>
    ///     Модель могли поправить между открытием окна и нажатием кнопки, а в совместной работе элемент
    ///     может быть занят. Такая строка попадает в отчёт, а прогон продолжается. Строка с дубликатами
    ///     применяется к каждому элементу отдельно: сбой на одном не останавливает остальные.
    /// </summary>
    private void Apply(ElementText text, Action<Element> change)
    {
        ApplyToOne(text.ElementId, text, change);

        foreach (var duplicateId in text.DuplicateElementIds)
        {
            ApplyToOne(duplicateId, text, change);
        }
    }

    private void ApplyToOne(ElementId elementId, ElementText text, Action<Element> change)
    {
        var element = document.GetElement(elementId);

        if (element is null)
        {
            failures.Add(string.Format(Text.FailureElementDeleted, text.Context));
            return;
        }

        // Занятый элемент срывает транзакцию, а с ней и всю пачку переводов, поэтому он
        // отбраковывается до записи, а не по факту исключения.
        if (ElementEditability.DescribeBlock(document, element) is { } block)
        {
            failures.Add($"{text.Context}: {block}");
            return;
        }

        try
        {
            change(element);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException exception)
        {
            failures.Add($"{text.Context}: {exception.Message}");
        }
    }
}
