using System;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.ClashNavigator.Models;
using Juru.RevitPlugins.ClashNavigator.Models.Interfaces;
using Juru.RevitPlugins.ClashNavigator.ViewModels.Navigator;

namespace Juru.RevitPlugins.ClashNavigator.Services.RevitViewSettings;

/// <summary>
/// Applies the navigator's view settings before the two clash elements are
/// selected. The repository performs the selection after this setting is run.
/// </summary>
internal sealed class ClashViewSettings : IView3DSetting {
    private readonly RevitRepository _repository;
    private readonly IClashViewModel _clash;
    private readonly SettingsConfig _settings;

    public ClashViewSettings(RevitRepository repository, object localizationService, IClashViewModel clash, SettingsConfig settings) {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _clash = clash ?? throw new ArgumentNullException(nameof(clash));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public void Apply(View3D view3D) {
        if(!_settings.ApplySectionBoxSettings) return;

        var bounds = _repository.GetCommonBoundingBox(_clash.GetElements());
        if(bounds is null) return;

        double offset = _settings.SectionBoxOffset / 2d / 304.8d;
        var min = bounds.Min - new XYZ(offset, offset, offset);
        var max = bounds.Max + new XYZ(offset, offset, offset);
        view3D.SetSectionBox(new BoundingBoxXYZ { Min = min, Max = max });
        view3D.IsSectionBoxActive = true;
    }
}
