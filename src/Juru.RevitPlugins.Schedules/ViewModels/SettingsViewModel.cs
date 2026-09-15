using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Juru.RevitPlugins.Schedules.Models;
using Juru.RevitPlugins.Schedules.Services;
using Juru.RevitPlugins.Schedules.Services.Translation;

using Juru.RevitPlugins.Schedules.Localization;

namespace Juru.RevitPlugins.Schedules.ViewModels;

/// <summary>
///     Настройки плагина: ключи служб перевода и размер пачки строк за запрос.
/// </summary>
/// <remarks>
///     Ключи не попадают в файл настроек — они пишутся в переменные окружения пользователя. Файл
///     настроек лежит рядом с плагином и попадает в репозиторий, а ключ туда попасть не должен.
/// </remarks>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SchedulesConfig config;

    [ObservableProperty]
    private int batchSize;

    /// <summary>
    ///     Писать в параметр исходник и перевод рядом, а не только перевод: тогда один и тот же
    ///     параметр читается и в русском, и в английском комплекте.
    /// </summary>
    [ObservableProperty]
    private bool keepSourceText;

    [ObservableProperty]
    private string separator;

    [ObservableProperty]
    private string deepLKeyVariable;

    [ObservableProperty]
    private string googleKeyVariable;

    /// <summary>Введённый ключ. Пусто означает «не менять»: показывать сохранённый ключ незачем.</summary>
    [ObservableProperty]
    private string deepLKey = string.Empty;

    [ObservableProperty]
    private string googleKey = string.Empty;

    [ObservableProperty]
    private string note = string.Empty;

    public SettingsViewModel(SchedulesConfig config)
    {
        this.config = config ?? throw new ArgumentNullException(nameof(config));

        var translation = config.Translation;

        batchSize = translation.BatchSize;
        keepSourceText = translation.KeepSourceText;
        separator = translation.Separator;
        deepLKeyVariable = translation.ResolveKeyVariable(TranslationSettings.DeepL);
        googleKeyVariable = translation.ResolveKeyVariable(TranslationSettings.Google);

    }

    /// <summary>Слово для проверки службы: короткое и заведомо переводимое.</summary>
    private const string Probe = "Помещение";

    /// <summary>Разделители, которые не путаются с текстом названия.</summary>
    public IReadOnlyList<SeparatorOption> Separators { get; } = TranslationLayout.SeparatorOptions;

    /// <summary>Путь файла, в который окно сохраняет настройки: видно, что и где меняется.</summary>
    public string ConfigPath
    {
        get { return SchedulesConfig.UserConfigPath; }
    }

    /// <summary>Есть ли уже сохранённый ключ DeepL. Сам ключ не показывается.</summary>
    public string DeepLKeyState
    {
        get { return DescribeKey(DeepLKeyVariable); }
    }

    public string GoogleKeyState
    {
        get { return DescribeKey(GoogleKeyVariable); }
    }

    /// <summary>Что записать в настройки, когда окно закрывают сохранением.</summary>
    public SchedulesConfig Result
    {
        get
        {
            return config with
            {
                Translation = config.Translation with
                {
                    BatchSize = Math.Max(1, BatchSize),
                    KeepSourceText = KeepSourceText,
                    Separator = Separator,
                    DeepLKeyEnvironmentVariable = DeepLKeyVariable.Trim(),
                    GoogleKeyEnvironmentVariable = GoogleKeyVariable.Trim()
                }
            };
        }
    }

    /// <summary>
    ///     Сохраняет ключи в переменные окружения пользователя. Пустое поле оставляет ключ как есть.
    /// </summary>
    /// <remarks>
    ///     Переменная ставится и процессу тоже: иначе служба перевода увидит новый ключ только после
    ///     перезапуска Ревита.
    /// </remarks>
    public void SaveKeys()
    {
        Save(DeepLKeyVariable, DeepLKey);
        Save(GoogleKeyVariable, GoogleKey);

        DeepLKey = string.Empty;
        GoogleKey = string.Empty;

        OnPropertyChanged(nameof(DeepLKeyState));
        OnPropertyChanged(nameof(GoogleKeyState));
    }

    /// <summary>
    ///     Проверяет службу одной короткой строкой: без этого о неверном ключе узнаёшь только на
    ///     полном прогоне, уже потратив время на чтение модели.
    /// </summary>
    [RelayCommand]
    private async Task CheckAsync()
    {
        SaveKeys();

        var translator = TranslatorFactory.TryCreate(Result.Translation, out var reason);

        if (translator is null)
        {
            Note = reason ?? Text.NoteServiceNotConfigured;
            return;
        }

        Note = string.Format(Text.NoteChecking, translator.Name);

        try
        {
            var translated = await translator
                .TranslateAsync([Probe], CancellationToken.None)
                .ConfigureAwait(true);

            Note = translated.Count > 0
                ? $"{translator.Name}: «{Probe}» → «{translated[0]}»"
                : string.Format(Text.NoteTranslatorEmpty, translator.Name);
        }
        catch (TranslationException exception)
        {
            Note = $"{translator.Name}: {exception.Message}";
        }
    }

    private static void Save(string variable, string key)
    {
        var trimmed = key.Trim();

        if (variable.Trim().Length is 0 || trimmed.Length is 0)
        {
            return;
        }

        Environment.SetEnvironmentVariable(variable, trimmed, EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable(variable, trimmed, EnvironmentVariableTarget.Process);
    }

    private static string DescribeKey(string variable)
    {
        if (variable.Trim().Length is 0)
        {
            return Text.SettingsVariableMissing;
        }

        var value = Environment.GetEnvironmentVariable(variable);

        return string.IsNullOrWhiteSpace(value) ? Text.SettingsKeyMissing : Text.SettingsKeySet;
    }

    partial void OnDeepLKeyVariableChanged(string value)
    {
        OnPropertyChanged(nameof(DeepLKeyState));
    }

    partial void OnGoogleKeyVariableChanged(string value)
    {
        OnPropertyChanged(nameof(GoogleKeyState));
    }
}
