# ClashNavigator

ClashNavigator finds, loads, and reviews geometric clashes between elements of a Revit model and its links.

The plugin ships four ribbon commands.
`FindClashesCommand` opens the checks window, which runs clash detection.
`GetClashesCommand` opens the navigator window over a clash report.
`SetFiltersCommand` opens the search set editor.
`SetSettingsCommand` opens the settings window.

`Juru.RevitPlugins.ClashNavigator` holds 11 026 lines across 169 files.
`Juru.RevitPlugins.Core` holds 1 463 lines and reimplements helper types under `dosymep.*` namespaces.
`Juru.RevitPlugins.App` holds the ribbon definition alone.

## Command startup

A command builds a Ninject `StandardKernel`, registers `RevitRepository`, `RevitEventHandler`, `ParameterFilterProvider`, the view model, and the window, then shows the window.

`GetClashesCommand` shows `NavigatorWindow` non-modally through `window.Show(uiApplication.MainWindowHandle)`.
The kernel outlives the command.

`SetSettingsCommand` shows `SettingsWindow` modally through `Notification(kernel.Get<SettingsWindow>())`.

`FindClashesCommand` and `SetFiltersCommand` show `MainWindow` and `FiltersWindow` non-modally.
Both bind `ChecksConfig` and `FiltersConfig` under `RevitRepository.GetConfigRelativePath()`, the path `ReportViewModel` also builds when it looks up the check behind a report.
`SetFiltersCommand` builds nothing itself: `SearchSetsWindowFactory` holds the kernel for `FiltersWindow`, because the checks window opens that window too.
The factory takes an existing `RevitRepository` when the caller has one - a second instance creates the clash 3D view in a transaction.

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

`ViewModels/SearchSet` edits that model.
`FiltersViewModel` holds the sets, `FilterViewModel` one set's categories and its root criterion, `SetViewModel` and `RuleViewModel` the tree below it.
`FilterViewModel.RefreshProviders` recomputes the parameters common to the chosen categories and keeps in the list any parameter a rule already refers to, so narrowing the categories cannot silently strip a rule of its parameter.
`RuleViewModel` fills its value list only when the list is dropped down: collecting values walks every document of the check.
An incomplete set does not save - `FiltersViewModel.CanSave` reports the first gap instead.
A set with categories and no rules is complete: `StraightRevitFilterGenerator` turns an empty criterion list into `ElementIsElementTypeFilter(true)`, which passes every instance of those categories.
An empty nested set is a gap, because under `Or` it would widen the set to those same instances.

## Localization

`assets/Localization/Language.ru-RU.xaml` and `Language.en-US.xaml` hold the UI strings.
`WpfLocalizationService` picks the file by appending the culture name as an extra extension to the
`resourceName` passed to `UseWpfLocalization`, and merges the default culture's dictionary in as a
fallback - so a key missing from the English file shows the Russian text rather than nothing.

Which culture that is comes from `ILanguageService.HostLanguage`: `UseWpfLocalization` reads it when
it builds the service, and `WpfLocalizationBehavior` reads it again when each window initializes to
fill in the `{me:LocalizationSource}` bindings in markup. `PluginLanguageService` implements that
interface over `SettingsConfig.Language`, so the plugin follows its own setting rather than Revit's
language; `kernel.UsePluginLanguage()` binds it and, the first time the setting is empty, asks
through `LanguagePromptWindow` - the one window that is deliberately bilingual, since it runs before
a language is chosen. The setting is changed afterwards in the settings window and takes effect the
next time a window opens.

`{me:LocalizationSource Key}` works only in windows implementing `IHasLocalization`, which is what
`WpfUIPlatformWindow` provides. The plain `Window`s - `SelectableNamesWindow`, `MessageWindow` - take
an `ILocalizationService` and set their captions from code instead.

Rule conditions live in the model (`RuleEvaluator.Message`), which is serialized into the filter
file, so `EvaluatorNames` rewrites those captions on the already-constructed objects rather than
storing keys: loading a set matches on the enum, never on the caption, so what a file holds there
does not matter.

## Navigator window

`NavigatorViewModel` holds the report list and exposes commands for loading a report file, loading from the server, deleting a report, saving all reports, selecting a clash, editing comments, and merging reports.

`ReportViewModel` holds the clashes of one report and their grid state.
`ClashViewModel` wraps one clash for the grid.
Selecting a clash calls `RevitRepository.SelectAndShowElement`.

`RevitEventHandler` runs Revit API work raised from the modeless window.

## Checks window

`MainViewModel` holds the checks, `CheckViewModel` one check, and `SelectionViewModel` one of its two selections.
A selection is a set of documents crossed with a set of search sets; `SelectionViewModel.GetProviders()` produces one `FilterProvider` per pair.
Both pickers are `SelectableNamesWindow`, a plain modal window rather than a `ui:ContentDialog`: a window has a title bar and Esc, so it closes even where the WPF-UI theme did not reach it.
The search set picker carries a create button; it closes the picker, opens the editor modally, re-reads `FiltersConfig`, and reopens the picker with the new set in the list, since with no sets saved there is nothing else to do there.
`CheckViewModel.GetProviders()` puts the selection that contains the active document first: detection collects elements in that document and intersects them with solids from the other side.
`SaveClashes` writes the result to `ClashesConfig` named `<document name>_<check name>`, carrying over statuses and comments from the previous run of the same check.

## Unreachable code

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
