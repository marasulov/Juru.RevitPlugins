using CommunityToolkit.Mvvm.ComponentModel;

using Juru.RevitPlugins.Schedules.Localization;
using Juru.RevitPlugins.Schedules.Models;
using Juru.RevitPlugins.Schedules.Services;

namespace Juru.RevitPlugins.Schedules.ViewModels;

/// <summary>
///     Строка словаря: русский текст и его перевод.
/// </summary>
public sealed partial class GlossaryRowViewModel : ObservableObject
{
    /// <summary>Перевод, с которым строка пришла из файла: по нему видно, правил ли её человек.</summary>
    private readonly string loaded;

    [ObservableProperty]
    private string sourceText;

    [ObservableProperty]
    private string translation;

    public GlossaryRowViewModel(GlossaryEntry entry)
    {
        sourceText = entry.Key;
        translation = entry.Suggestion;
        loaded = entry.Suggestion;

        Readable = entry.Readable;
        Occurrences = entry.Occurrences;
        Schedules = string.Join(", ", entry.Schedules);
        Source = entry.Source;
        IsNew = false;
    }

    /// <summary>Пустая строка, которую пользователь заполнит сам.</summary>
    public GlossaryRowViewModel()
    {
        sourceText = string.Empty;
        translation = string.Empty;
        loaded = string.Empty;

        Readable = string.Empty;
        Occurrences = 0;
        Schedules = string.Empty;
        Source = TranslationSource.Human;
        IsNew = true;
    }

    public string Readable { get; }

    public int Occurrences { get; }

    public string Schedules { get; }

    public TranslationSource Source { get; }

    public bool IsNew { get; }

    /// <summary>Перевод правили в окне: тогда он человеческий, чем бы ни был раньше.</summary>
    public bool IsEdited
    {
        get { return !string.Equals(Translation.Trim(), loaded.Trim(), System.StringComparison.CurrentCulture); }
    }

    public string OriginNote
    {
        get
        {
            if (IsNew || IsEdited)
            {
                return Text.OriginHuman;
            }

            return Source switch
            {
                TranslationSource.Machine => Text.OriginMachine,
                TranslationSource.Base => Text.OriginBase,
                TranslationSource.Derived => Text.OriginDerived,
                _ => Text.OriginHuman
            };
        }
    }

    /// <summary>
    ///     Строка для записи в файл. Правленная руками помечается человеческой: по этой пометке
    ///     машинный проход её не перетирает.
    /// </summary>
    public GlossaryEntry ToEntry()
    {
        var key = SourceText.Trim();

        return new GlossaryEntry
        {
            Key = key,
            Readable = Readable.Length > 0 && !IsEdited && !IsNew ? Readable : RussianText.Normalize(key),
            Suggestion = Translation.Trim(),
            Source = IsNew || IsEdited ? TranslationSource.Human : Source,
            Occurrences = Occurrences,
            Schedules = Schedules.Length is 0
                ? []
                : Schedules.Split(',').Select(part => part.Trim()).Where(part => part.Length > 0).ToArray()
        };
    }

    public bool Matches(string search)
    {
        return search.Length is 0
               || SourceText.IndexOf(search, System.StringComparison.CurrentCultureIgnoreCase) >= 0
               || Translation.IndexOf(search, System.StringComparison.CurrentCultureIgnoreCase) >= 0;
    }
}
