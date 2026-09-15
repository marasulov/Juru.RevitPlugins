using Autodesk.Revit.DB;

namespace Juru.RevitPlugins.Schedules.Services;

/// <summary>
///     Разбирает отказы Ревита во время записи перевода.
/// </summary>
/// <remarks>
///     Без него одно предупреждение по ходу записи откатывает всю транзакцию, и перевод «просто
///     не работает» без объяснения. Предупреждения снимаются, ошибки — разрешаются, если Ревит
///     предлагает решение, а неразрешимая ошибка откатывает транзакцию честно, а не молча.
///     Тот же приём в эталоне.
/// </remarks>
public sealed class TranslationFailures : IFailuresPreprocessor
{
    public FailureProcessingResult PreprocessFailures(FailuresAccessor failures)
    {
        var resolved = false;

        foreach (var failure in failures.GetFailureMessages())
        {
            var severity = failure.GetSeverity();

            if (severity is FailureSeverity.Warning)
            {
                failures.DeleteWarning(failure);
                continue;
            }

            if (severity is not FailureSeverity.Error)
            {
                continue;
            }

            if (!failure.HasResolutions())
            {
                return FailureProcessingResult.ProceedWithRollBack;
            }

            failures.ResolveFailure(failure);
            resolved = true;
        }

        return resolved
            ? FailureProcessingResult.ProceedWithCommit
            : FailureProcessingResult.Continue;
    }
}

/// <summary>
///     Можно ли править элемент в модели совместной работы.
/// </summary>
/// <remarks>
///     Проверка идёт до записи, а не по факту исключения: занятый другим пользователем элемент
///     иначе срывает транзакцию, и вместе с ним теряется вся пачка переводов. Тот же порядок
///     в эталоне.
/// </remarks>
public static class ElementEditability
{
    /// <summary>Причина, по которой элемент не изменить. <c>null</c> — изменять можно.</summary>
    public static string? DescribeBlock(Document document, Element element)
    {
        if (!element.IsValidObject)
        {
            return "элемент удалён из модели";
        }

        if (!document.IsWorkshared)
        {
            return null;
        }

        try
        {
            if (WorksharingUtils.GetCheckoutStatus(document, element.Id) is CheckoutStatus.OwnedByOtherUser)
            {
                var owner = TryGetOwner(document, element);

                return owner is null ? "занят другим пользователем" : $"занят пользователем {owner}";
            }

            if (WorksharingUtils.GetModelUpdatesStatus(document, element.Id)
                is ModelUpdatesStatus.DeletedInCentral)
            {
                return "удалён в центральной модели";
            }
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            // Состояние совместной работы недоступно - пробуем записать и разбираемся по факту.
            return null;
        }

        return null;
    }

    private static string? TryGetOwner(Document document, Element element)
    {
        try
        {
            return WorksharingUtils.GetWorksharingTooltipInfo(document, element.Id).Owner;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return null;
        }
    }
}
