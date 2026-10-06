using System;
using System.Xml.Linq;

using Juru.RevitPlugins.Core.ProjectConfigs;

using dosymep.SimpleServices;

using Juru.RevitPlugins.ClashNavigator.Models;

namespace Juru.RevitPlugins.ClashNavigator.ViewModels.Services;
internal class ConfigSaverService {
    private readonly RevitRepository _revitRepository;

    public ConfigSaverService(RevitRepository revitRepository, ISaveFileDialogService saveFileDialogService) {
        _revitRepository = revitRepository ?? throw new ArgumentNullException(nameof(revitRepository));
        SaveFileDialogService = saveFileDialogService ?? throw new ArgumentNullException(nameof(saveFileDialogService));
    }

    public ISaveFileDialogService SaveFileDialogService { get; }

    public void Save(ProjectConfig config) {
        SaveFileDialogService.AddExtension = true;
        SaveFileDialogService.Filter = "ClashConfig |*.json";
        SaveFileDialogService.FilterIndex = 1;

        if(!SaveFileDialogService.ShowDialog(_revitRepository.GetFileDialogPath(), "config")) {
            throw new OperationCanceledException();
        }
        var configSaver = new ConfigSaver(_revitRepository.Doc);
        configSaver.Save(config, SaveFileDialogService.File.FullName);

        _revitRepository.CommonConfig.LastRunPath = SaveFileDialogService.File.DirectoryName;
        _revitRepository.CommonConfig.SaveProjectConfig();
    }

    /// <summary>
    /// Спрашивает путь и записывает файл обмена Navisworks.
    /// </summary>
    /// <returns>Путь записанного файла.</returns>
    /// <exception cref="OperationCanceledException">Пользователь закрыл диалог.</exception>
    public string SaveXml(XDocument document, string defaultName) {
        SaveFileDialogService.AddExtension = true;
        SaveFileDialogService.Filter = "Navisworks (*.xml)|*.xml";
        SaveFileDialogService.FilterIndex = 1;

        if(!SaveFileDialogService.ShowDialog(_revitRepository.GetFileDialogPath(), defaultName)) {
            throw new OperationCanceledException();
        }
        document.Save(SaveFileDialogService.File.FullName);

        _revitRepository.CommonConfig.LastRunPath = SaveFileDialogService.File.DirectoryName;
        _revitRepository.CommonConfig.SaveProjectConfig();
        return SaveFileDialogService.File.FullName;
    }
}
