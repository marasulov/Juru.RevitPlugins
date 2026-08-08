# ClashNavigator

ClashNavigator finds, loads, and reviews geometric clashes between elements of a Revit model and its links.

The plugin ships two ribbon commands.
`GetClashesCommand` opens the navigator window over a clash report.
`SetSettingsCommand` opens the settings window.

`Juru.RevitPlugins.ClashNavigator` holds 11 026 lines across 169 files.
`Juru.RevitPlugins.Core` holds 1 463 lines and reimplements helper types under `dosymep.*` namespaces.
`Juru.RevitPlugins.App` holds the ribbon definition alone.

## Command startup

A command builds a Ninject `StandardKernel`, registers `RevitRepository`, `RevitEventHandler`, `ParameterFilterProvider`, the view model, and the window, then shows the window.

`GetClashesCommand` shows `NavigatorWindow` non-modally through `window.Show(uiApplication.MainWindowHandle)`.
The kernel outlives the command.

`SetSettingsCommand` shows `SettingsWindow` modally through `Notification(kernel.Get<SettingsWindow>())`.

## RevitRepository

`RevitRepository` is the single boundary to the Revit API.
It wraps the active `Document`, the `UIApplication`, and the list of `DocInfo` entries describing the main document and its links.

It owns document naming, link enumeration, element lookup across documents, category and parameter enumeration, unit conversion, and the clash view.

`GetClashView()` returns the 3D view named `BIM_Проверка на коллизии`.
`SelectAndShowElement` and `ShowElements` select elements on that view and apply a section box.
`RemoveFilters` strips view filters whose names start with `BIM_коллизии_`.

`GetLevelName(Element)` probes 16 `BuiltInParameter` values in order.
Callers that do not display the level pass `computeLevel: false` to `ElementModel`.

## Clash detection

`ClashDetector` takes providers for the main document and providers for the other documents.
It runs `ProvidersClashDetector` for every provider pair, concatenates the results, removes duplicates, and names each clash `Конфликт<N>`.

`IProvider` exposes `Doc`, `MainTransform`, `GetElements()`, and `GetSolids(Element)`.
`FilterProvider` implements it over a saved `Filter`.

`ProvidersClashDetector` iterates the second provider's elements.
For each element it transforms the solids into the first provider's coordinate space through `MainTransform.GetTransitionMatrix`.
Closed solids go through `GetElementClashesWithIntersection`, which runs a `BoundingBoxIntersectsFilter` and then a boolean intersection whose volume must exceed zero.
Open solids go through `GetElementClashesWithFilter`, which runs `ElementIntersectsSolidFilter`.
Solid closedness is evaluated before transformation.

A failed boolean operation counts as a clash.
`ClashData` records the intersection volume for later review.

Progress and cancellation come from `IProgressDialogService`, resolved through the static `ServicesProvider`.

`ClashesMarker.MarkSolvedClashes` copies status, name, and comments from a previous run onto matching new clashes.

## Clash model

`ClashModel` holds `MainElement`, `OtherElement`, `Name`, `ClashStatus`, `ClashPoint`, `ImagePath`, and a set of `ClashComment`.
Two clashes are equal when their element pairs match in either order.

`ClashStatus` has four values: `Active`, `Analized`, `Solved`, and `Imaginary`.
Display names come from the `[Display]` attribute through `ClashStatusExtensions.GetDisplayName()`.

`GetClashData()` recomputes solid volumes and their intersection.
It requires a repository assigned through `SetRevitRepository`.
Geometry failures return `ClashData` with zero volumes or `InvalidClashData`.

`ElementModel` carries the document name, the element id, and the transform.

## Report loading

`ReportLoader.GetReports(RevitRepository, path)` dispatches on file type.

`NavisXmlClashesLoader` reads a Navisworks XML clash report.
It resolves element ids from the report, reads the clash point in metres, and reads the exported screenshot path.

`PluginClashesLoader` reads a report the plugin itself saved as JSON.

`ServerReportLoader` reads clashes from a REST service.
`RevitFileNameInfo.TryParse` derives the project code, object name, discipline, and Revit version from the open file name.
`ClashTestNameMatchesDiscipline` keeps only the clash tests matching that discipline.
`ClashServerClient` caches one `HttpClient` per base URL and disables proxy detection.

A `ReportModel` pairs a report name with its clashes.

## Filters

`Filter` holds a name, a category id list, and a root `Set`.
`Set` and `Rule` derive from `Criterion`.
`Set` combines child criteria with `SetEvaluator`.
`Rule` compares a value from `IFilterableValueProvider` against `ParamValue` using `RuleEvaluator`.

`Filter.GetRevitFilter(Document, IFilterGenerator)` produces an `ElementFilter`.
`StraightRevitFilterGenerator` produces the filter used during detection.
The visitor types under `Models/Visiter` walk the criterion tree.

`FiltersConfig` and `ClashesConfig` derive from `ProjectConfig` and persist as JSON under `%MyDocuments%\dosymep\<RevitVersion>\ClashNavigator\`.

## Navigator window

`NavigatorViewModel` holds the report list and exposes commands for loading a report file, loading from the server, deleting a report, saving all reports, selecting a clash, editing comments, and merging reports.

`ReportViewModel` holds the clashes of one report and their grid state.
`ClashViewModel` wraps one clash for the grid.
Selecting a clash calls `RevitRepository.SelectAndShowElement`.

`RevitEventHandler` runs Revit API work raised from the modeless window.

## Unreachable code

The ribbon exposes `GetClashesCommand` and `SetSettingsCommand`.
A reachability walk from those two roots over non-comment source reaches 123 of 183 files.
Of the 60 remaining files, 53 hold no reachable type, and they total about 3 000 lines.

`CreateFiltersCommand.cs` and `DetectiveClashesCommand.cs` are commented out in full.
`NavigatorViewModel.OpenClashDetector()` has an empty body.
Clash detection therefore has no entry point at runtime.
`ClashDetector`, `ProvidersClashDetector`, and `FilterProvider` run only from `ViewModels/ClashDetective`, which only those two entry points reach.
The plugin loads and reviews reports; it does not produce them.

The filter editor is unreachable as a whole: `ViewModels/FilterCreatorViewModels` at 14 files, `Views/Views/Filters` at 10, and `ViewModels/SearchSet` at 4.

`Models/RevitClashes/Models/RevitViewSettings` at 6 files, `View3DBuilder`, `IView3DBuilder`, and `IView3DProvider` form a second view-construction subsystem that nothing instantiates.
`Services/RevitViewSettings/ClashViewSettings` is the live `IView3DSetting` implementation.

`Juru.RevitPlugins.ClashNavigatorView` and `Juru.RevitPlugins.ClashNavigatorViewModel` are template scaffolding.
`Resources/ClashStatusResources.xaml` and `Resources/CustomDataGrid.xaml` have no reference.

The five classes under `Models/RevitClashes/Models/Extensions` stay live through extension-method syntax.
Individual members of them are unused: `GetIntersectedBb`, `GetIntersection`, `GetUnitedBb`, `IntersectedWith`, `IsIntersected`, `GetSolid`, `GetUnitedSolids`, and `DrowSolid`.

## Dependencies

The plugin references five vendored `dosymep` assemblies plus `Bim4Everyone.RevitFiltration`, `pyRevitLabs.Json`, and `Serilog`.

`dosymep.Bim4Everyone.dll` and `dosymep.Revit.dll` carry the same assembly version as the copies installed with the Bim4Everyone product and different content.
The .NET Framework loader binds by simple name and version.
The copy loaded first wins for the whole process.
See the notes on dependency isolation for the resulting failure modes.

`dosymep.SimpleServices.dll`, `dosymep.WpfCore.dll`, and `dosymep.WpfUI.Core.dll` are byte-identical to the installed copies.

The types used from the two conflicting assemblies are `IConfigSerializer` at 20 sites, `ServicesProvider` at 14, `ModuleEnvironment` at 13, `ProjectConfig` at 11, `SystemParams` at 5, `ProjectParams` at 3, and `SharedParams` at 2.
`dosymep.Revit` supplies Revit extension methods to 17 files.

The clash detection, filter, report, and view model code depends on the Revit API and WPF alone.
