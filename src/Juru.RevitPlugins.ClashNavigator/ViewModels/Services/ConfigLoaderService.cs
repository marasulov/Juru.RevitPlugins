using System;

using Juru.RevitPlugins.Core.ProjectConfigs;

using dosymep.SimpleServices;

using Juru.RevitPlugins.ClashNavigator.Models;
using Juru.RevitPlugins.ClashNavigator.Models.FilterModel;

namespace Juru.RevitPlugins.ClashNavigator.ViewModels.Services;
internal class ConfigLoaderService {
    private readonly RevitRepository _revitRepository;
    private readonly ILocalizationService _localization;

    public ConfigLoaderService(RevitRepository revitRepository,
        ILocalizationService localization,
        IOpenFileDialogService openFileDialogService,
        IMessageBoxService messageBoxService) {
        _revitRepository = revitRepository ?? throw new ArgumentNullException(nameof(revitRepository));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        OpenFileDialogService = openFileDialogService ?? throw new ArgumentNullException(nameof(openFileDialogService));
        MessageBoxService = messageBoxService ?? throw new ArgumentNullException(nameof(messageBoxService));
    }

    public IOpenFileDialogService OpenFileDialogService { get; }
    public IMessageBoxService MessageBoxService { get; }


    public T Load<T>() where T : ProjectConfig, new() {
        OpenFileDialogService.Filter = "ClashConfig |*.json";


        if(!OpenFileDialogService.ShowDialog(_revitRepository.GetFileDialogPath())) {
            throw new OperationCanceledException();
        }

        _revitRepository.CommonConfig.LastRunPath = OpenFileDialogService.File.DirectoryName;
        _revitRepository.CommonConfig.SaveProjectConfig();

        try {
            var configLoader = new ConfigLoader(_revitRepository.Doc);
            return configLoader.Load<T>(OpenFileDialogService.File.FullName);
        } catch(Exception) {
            ShowError();
            throw new OperationCanceledException();
        }
    }

    public void CheckConfig(FiltersConfig filtersConfig) {
        if(filtersConfig.Filters.Count == 0 && string.IsNullOrEmpty(filtersConfig.RevitVersion)) {
            ShowError();
            throw new OperationCanceledException();
        }
    }

    public void CheckConfig(ChecksConfig checksConfig) {
        if(checksConfig.Checks.Count == 0 && string.IsNullOrEmpty(checksConfig.RevitVersion)) {
            ShowError();
            throw new OperationCanceledException();
        }
    }

    /// <summary>
    /// <c>Views.MessageWindow</c>, а не <c>IMessageBoxService</c> из WPF-UI: в окнах проверок и
    /// поисковых наборов тот рисует пустую плашку без текста и кнопок - см.
    /// <c>MainViewModel.AskForSave</c>.
    /// </summary>
    private void ShowError() {
        Views.MessageWindow.ShowError(
            _localization.GetLocalizedString("ConfigLoader.InvalidFile"),
            _localization.GetLocalizedString("BIM"),
            _localization);
    }
}
