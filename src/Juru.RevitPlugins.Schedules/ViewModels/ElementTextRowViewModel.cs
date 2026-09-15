using System;
using System.Collections.Generic;

using CommunityToolkit.Mvvm.ComponentModel;

using Juru.RevitPlugins.Schedules.Models;

namespace Juru.RevitPlugins.Schedules.ViewModels;

/// <summary>
///     Строка таблицы категории: один текст одного элемента модели. В отличие от строки глоссария, здесь
///     перевод привязан к элементу, а не к тексту: одинаковые названия у разных помещений переводятся
///     по отдельности, потому что записываются по отдельности.
/// </summary>
public sealed partial class ElementTextRowViewModel : ObservableObject, ISelectableRow
{
    [ObservableProperty]
    private bool isSelected;

    private readonly Func<TranslationLayout> layout;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    [NotifyPropertyChangedFor(nameof(Result))]
    private string translation;

    /// <param name="layout">
    ///     Как собирается записываемое значение. Берётся при каждом обращении: пользователь меняет
    ///     режим и разделитель, когда таблица уже построена.
    /// </param>
    public ElementTextRowViewModel(
        ElementText text,
        IReadOnlyDictionary<string, string> glossary,
        Func<TranslationLayout> layout)
    {
        Text = text;
        this.layout = layout;

        // У переведённого элемента в параметре стоит английская строка - она и есть текущий перевод,
        // иначе окно предложило бы заменить её на глоссарный вариант и потеряло правку. Исходник из
        // двуязычного значения при этом отбрасывается, иначе следующая запись удвоила бы его.
        translation = text.IsTranslated
            ? TranslationLayout.StripSource(text.SourceText, text.CurrentText)
            : glossary.TryGetValue(text.SourceText, out var suggestion) ? suggestion : string.Empty;
    }

    public ElementText Text { get; }

    public string Context
    {
        get { return Text.Context; }
    }

    /// <summary>Русский исходник: у переведённого элемента - сохранённый, у остальных - текущее значение.</summary>
    public string SourceText
    {
        get { return Text.SourceText; }
    }

    public bool HasTranslation
    {
        get { return Translation.Trim().Length > 0; }
    }

    /// <summary>Что окажется в параметре элемента: перевод либо исходник и перевод через разделитель.</summary>
    public string Result
    {
        get { return layout().Compose(Text.SourceText, Translation); }
    }

    /// <summary>Сообщает строке, что режим записи или разделитель изменились.</summary>
    public void RefreshResult()
    {
        OnPropertyChanged(nameof(Result));
    }

    public string Status
    {
        get
        {
            if (!HasTranslation)
            {
                return Text.IsTranslated
                    ? Localization.Text.StatusWillRevert
                    : Localization.Text.StatusNeedsTranslation;
            }

            return Text.IsTranslated
                ? Localization.Text.StatusTranslatedInModel
                : Localization.Text.StatusReadyToWrite;
        }
    }
}
