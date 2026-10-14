using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.SystemViews.Localization;
using Juru.RevitPlugins.SystemViews.Models;

namespace Juru.RevitPlugins.SystemViews.Services;

/// <summary>
///     Создаёт и обновляет 3D-схемы систем.
/// </summary>
/// <remarks>
///     <para>
///         Схема - копия исходного 3D-вида с фильтром, который скрывает всё, что не относится к
///         системе: элементы чужих систем, элементы с пустым параметром, а категории, где параметра
///         нет вовсе, скрываются на виде целиком. Фильтр, а не скрытие элементов по одному: новые
///         элементы системы появляются на схеме сами.
///     </para>
///     <para>
///         Каждая схема строится в своей транзакции внутри одной группы: ошибка в одной системе не
///         откатывает остальные, а отмена в Ревите снимает весь запуск одним шагом.
///     </para>
/// </remarks>
public sealed class SystemViewBuilder
{
    /// <summary>Запас 3D-рамки вокруг элементов системы, мм.</summary>
    private const double SectionBoxMarginMm = 500;

    /// <summary>Наименьший запас области вида вокруг системы, мм.</summary>
    private const double CropMarginMm = 1500;

    /// <summary>Запас области вида от размера системы: у большой системы и выноски длиннее.</summary>
    private const double CropMarginShare = 0.1;

    private readonly Document document;
    private readonly SourceViewContent source;

    public SystemViewBuilder(Document document, SourceViewContent source)
    {
        this.document = document ?? throw new ArgumentNullException(nameof(document));
        this.source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public SystemViewsReport Build(SystemViewsRequest request)
    {
        var filterCategories = FilterCategories(request);
        var hiddenCategories = request.Categories.Except(request.Parameter.Categories).ToList();

        // Видимы на схеме только категории, которые отфильтрованы по системе: всё остальное -
        // архитектура, конструкции, чужие разделы, связи - скрыто.
        var visibleCategories = new HashSet<ElementId>(filterCategories);

        var baseTemplate = ResolveTemplate(request);

        var outcomes = new List<SystemViewOutcome>();

        using (var group = new TransactionGroup(document, Text.TransactionName))
        {
            group.Start();

            foreach (var system in request.Systems)
            {
                outcomes.Add(BuildOne(request, system, filterCategories, visibleCategories, baseTemplate));
            }

            if (outcomes.Any(outcome => outcome.Succeeded))
            {
                group.Assimilate();
            }
            else
            {
                group.RollBack();
            }
        }

        return new SystemViewsReport
        {
            Outcomes = outcomes,
            HiddenCategories = hiddenCategories
                .Select(category => Category.GetCategory(document, category)?.Name ?? category.ToString())
                .ToList(),
            BaseTemplate = baseTemplate?.Name
        };
    }

    private SystemViewOutcome BuildOne(
        SystemViewsRequest request,
        string system,
        IList<ElementId> filterCategories,
        ISet<ElementId> visibleCategories,
        View? baseTemplate)
    {
        var viewName = SystemNames.ViewName(request.ViewNamePrefix, system);

        using var transaction = new Transaction(document, viewName);

        try
        {
            transaction.Start();

            var view = FindView(viewName);
            var created = view is null;

            view ??= Duplicate(viewName);

            if (view.IsLocked)
            {
                view.Unlock();
            }

            var filter = UpsertFilter(request.Parameter, system, filterCategories);
            var elements = source.ElementsOf(request.Parameter, system);

            if (baseTemplate is null)
            {
                // Без шаблона схема настраивается на самом виде.
                view.ViewTemplateId = ElementId.InvalidElementId;
                ShowOnly(view, filter);
                ShowSchemeOnly(view, visibleCategories);
                RevealSystem(view, filter, elements);
            }
            else
            {
                // Как шаблоны «..._Система_В1», сделанные вручную: свой шаблон на систему - копия
                // базового плюс фильтр системы. Вид остаётся под шаблоном, а правка базового
                // шаблона доходит до схем повторным запуском.
                var template = UpsertSystemTemplate(view, baseTemplate, system);
                ControlVisibility(template);
                ShowOnly(template, filter);
                ShowSchemeOnly(template, visibleCategories);
                RevealSystem(template, filter, elements);
                view.ViewTemplateId = template.Id;
            }

            if (request.FitSectionBox)
            {
                FitSectionBox(view, elements);
            }

            FitCropBox(view, elements);

            if (!view.IsPerspective)
            {
                view.SaveOrientationAndLock();
            }

            transaction.Commit();

            return new SystemViewOutcome(system, viewName, created);
        }
        catch (Exception exception) when (exception is Autodesk.Revit.Exceptions.ApplicationException
                                              or InvalidOperationException
                                              or ArgumentException)
        {
            if (transaction.HasStarted())
            {
                transaction.RollBack();
            }

            return new SystemViewOutcome(system, viewName, false, exception.Message);
        }
    }

    /// <summary>
    ///     Категории фильтра: выбранные, по которым параметром можно фильтровать вид.
    /// </summary>
    private IList<ElementId> FilterCategories(SystemViewsRequest request)
    {
        var filterable = ParameterFilterUtilities.GetAllFilterableCategories();

        return request.Parameter.Categories
            .Select(category => new ElementId(category))
            .Where(filterable.Contains)
            .ToList();
    }

    private View? ResolveTemplate(SystemViewsRequest request)
    {
        var id = request.TemplateMode switch
        {
            TemplateMode.AsSourceView => source.SourceView.ViewTemplateId,
            TemplateMode.Specific when request.TemplateId is { } templateId => ElementIds.FromLong(templateId),
            _ => ElementId.InvalidElementId
        };

        return id == ElementId.InvalidElementId ? null : document.GetElement(id) as View;
    }

    /// <summary>
    ///     Шаблон системы: создаётся из базового при первом запуске, при повторном заново берёт
    ///     настройки базового - так правка базового шаблона доходит до всех схем.
    /// </summary>
    /// <remarks>
    ///     Revit API не копирует шаблон напрямую: <c>Duplicate</c> у шаблона запрещён. Поэтому
    ///     настройки базового переносятся на вид схемы, и уже из него делается шаблон.
    /// </remarks>
    private View UpsertSystemTemplate(View view, View baseTemplate, string system)
    {
        var name = SystemNames.TemplateName(baseTemplate.Name, system);

        var template = new FilteredElementCollector(document)
            .OfClass(typeof(View))
            .Cast<View>()
            .FirstOrDefault(candidate => candidate.IsTemplate && candidate.Name == name);

        if (template is not null)
        {
            template.ApplyViewTemplateParameters(baseTemplate);
            return template;
        }

        view.ViewTemplateId = ElementId.InvalidElementId;
        view.ApplyViewTemplateParameters(baseTemplate);

        template = view.CreateViewTemplate();
        template.Name = name;

        return template;
    }

    /// <summary>
    ///     Шаблон системы обязан сам решать видимость модели, фильтров, связей и аннотаций: иначе
    ///     то, что базовый шаблон оставил на усмотрение вида, на схеме снова будет видно.
    /// </summary>
    private static void ControlVisibility(View template)
    {
        var free = template.GetNonControlledTemplateParameterIds();

        var controlled = new[]
        {
            BuiltInParameter.VIS_GRAPHICS_MODEL,
            BuiltInParameter.VIS_GRAPHICS_FILTERS,
            BuiltInParameter.VIS_GRAPHICS_ANNOTATION,
            BuiltInParameter.VIS_GRAPHICS_RVT_LINKS,
            BuiltInParameter.VIS_GRAPHICS_IMPORT
        }.Select(parameter => new ElementId(parameter)).ToList();

        if (free.Any(controlled.Contains))
        {
            template.SetNonControlledTemplateParameterIds(free.Where(id => !controlled.Contains(id)).ToList());
        }
    }

    /// <summary>
    ///     Оставляет на виде только категории схемы: прочие категории модели, связи, импорт и
    ///     служебную графику (уровни, оси, рамки) скрывает, категории схемы - показывает, даже если
    ///     базовый шаблон их прятал.
    /// </summary>
    private void ShowSchemeOnly(View view, ISet<ElementId> visibleCategories)
    {
        foreach (Category category in document.Settings.Categories)
        {
            if (category.CategoryType is not CategoryType.Model && !ServiceCategories.Contains(category.Id))
            {
                continue;
            }

            SetHidden(view, category.Id, !visibleCategories.Contains(category.Id));
        }

        foreach (var id in ServiceCategories)
        {
            SetHidden(view, id, true);
        }
    }

    /// <summary>
    ///     Показывает скрытые фильтры оформления, которые прячут элементы самой системы. Схема
    ///     показывает систему целиком, а базовый шаблон мог прятать её часть: например,
    ///     <c>U_HVAC_O_CX_Вентиляция</c> скрывает всё вентиляционное оборудование.
    /// </summary>
    /// <remarks>
    ///     Чужие системы при этом не появляются: элемент скрыт, если его прячет хоть один фильтр, а
    ///     их прячет фильтр системы. Меняется только шаблон этой схемы, базовый - нет.
    /// </remarks>
    private void RevealSystem(View view, ParameterFilterElement own, IReadOnlyList<Element> elements)
    {
        foreach (var id in view.GetFilters())
        {
            if (id == own.Id || view.GetFilterVisibility(id) || document.GetElement(id) is not ParameterFilterElement filter)
            {
                continue;
            }

            var categories = filter.GetCategories();
            var rules = filter.GetElementFilter();

            var hidesSystem = elements.Any(element => element.Category is { } category
                                                      && categories.Contains(category.Id)
                                                      && (rules is null || rules.PassesFilter(element)));

            if (hidesSystem)
            {
                view.SetFilterVisibility(id, true);
            }
        }
    }

    private static void SetHidden(View view, ElementId category, bool hidden)
    {
        if (!view.CanCategoryBeHidden(category) || view.GetCategoryHidden(category) == hidden)
        {
            return;
        }

        view.SetCategoryHidden(category, hidden);
    }

    /// <summary>Не модель, но на схеме лишнее.</summary>
    private static readonly HashSet<ElementId> ServiceCategories = new[]
    {
        BuiltInCategory.OST_RvtLinks,
        BuiltInCategory.OST_Levels,
        BuiltInCategory.OST_Grids,
        BuiltInCategory.OST_SectionBox,
        BuiltInCategory.OST_VolumeOfInterest
    }.Select(category => new ElementId(category)).ToHashSet();

    private View3D? FindView(string name)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(View3D))
            .Cast<View3D>()
            .FirstOrDefault(view => !view.IsTemplate && view.Name == name);
    }

    private View3D Duplicate(string name)
    {
        var sourceView = source.SourceView;

        if (!sourceView.CanViewBeDuplicated(ViewDuplicateOption.Duplicate))
        {
            throw new InvalidOperationException(Text.CannotDuplicate);
        }

        var view = (View3D) document.GetElement(sourceView.Duplicate(ViewDuplicateOption.Duplicate));
        view.Name = name;

        return view;
    }

    /// <summary>
    ///     Фильтр, под который попадает всё, что не относится к системе. Один на систему, правила
    ///     переписываются при каждом запуске: параметр системы мог смениться.
    /// </summary>
    private ParameterFilterElement UpsertFilter(SystemParameterInfo parameter, string system, IList<ElementId> categories)
    {
        if (categories.Count is 0)
        {
            throw new InvalidOperationException(string.Format(Text.ParameterNotFilterable, parameter.Name));
        }

        var elementFilter = NotSystemFilter(ElementIds.FromLong(parameter.Id), system);

        if (!ParameterFilterElement.ElementFilterIsAcceptableForParameterFilterElement(document, new HashSet<ElementId>(categories), elementFilter))
        {
            throw new InvalidOperationException(string.Format(Text.ParameterNotFilterable, parameter.Name));
        }

        var name = SystemNames.FilterName(system);

        var filter = new FilteredElementCollector(document)
            .OfClass(typeof(ParameterFilterElement))
            .Cast<ParameterFilterElement>()
            .FirstOrDefault(element => element.Name == name);

        if (filter is null)
        {
            return ParameterFilterElement.Create(document, name, categories, elementFilter);
        }

        // Старые правила могли ссылаться на параметр, которого у новых категорий нет: сначала они
        // снимаются, затем ставятся категории и новые правила.
        filter.ClearRules();
        filter.SetCategories(categories);
        filter.SetElementFilter(elementFilter);

        return filter;
    }

    /// <summary>
    ///     «Не система»: параметр пуст, или ни одно из условий <see cref="SystemNames.MatchPatterns" />
    ///     не выполняется.
    /// </summary>
    private static ElementFilter NotSystemFilter(ElementId parameterId, string system)
    {
        var notRules = SystemNames.MatchPatterns(system)
            .Select(pattern => NotRule(parameterId, pattern))
            .ToList();

        return new LogicalOrFilter(
            new ElementParameterFilter(ParameterFilterRuleFactory.CreateHasNoValueParameterRule(parameterId)),
            new ElementParameterFilter(notRules));
    }

    private static FilterRule NotRule(ElementId parameterId, MatchPattern pattern)
    {
        return pattern.Kind switch
        {
            MatchKind.Equals => ParameterFilterRuleFactory.CreateNotEqualsRule(parameterId, pattern.Text),
            MatchKind.BeginsWith => ParameterFilterRuleFactory.CreateNotBeginsWithRule(parameterId, pattern.Text),
            MatchKind.EndsWith => ParameterFilterRuleFactory.CreateNotEndsWithRule(parameterId, pattern.Text),
            MatchKind.Contains => ParameterFilterRuleFactory.CreateNotContainsRule(parameterId, pattern.Text),
            _ => throw new ArgumentOutOfRangeException(nameof(pattern))
        };
    }

    /// <summary>
    ///     Ставит фильтр системы и скрывает его, а фильтры прошлых запусков для других систем снимает:
    ///     вид мог быть переименован под другую систему или скопирован со схемы.
    /// </summary>
    private void ShowOnly(View view, ParameterFilterElement filter)
    {
        foreach (var id in view.GetFilters().ToList())
        {
            if (id != filter.Id && document.GetElement(id) is ParameterFilterElement other
                                && other.Name.StartsWith(SystemNames.FilterPrefix, StringComparison.Ordinal))
            {
                view.RemoveFilter(id);
            }
        }

        if (!view.IsFilterApplied(filter.Id))
        {
            view.AddFilter(filter.Id);
        }

        view.SetFilterVisibility(filter.Id, false);
    }

    /// <summary>
    ///     Ставит рамку обрезки вокруг системы и выключает обрезку. Без этого схема наследует
    ///     обрезку исходного вида, а та стоит над другим местом здания - система обрезается или не
    ///     попадает в кадр вовсе.
    /// </summary>
    /// <remarks>
    ///     Берутся только элементы, которые на схеме видны: оборудование, спрятанное фильтром
    ///     шаблона, не раздувает рамку. Рамка строится в осях вида, глубина остаётся прежней.
    /// </remarks>
    private void FitCropBox(View3D view, IReadOnlyList<Element> elements)
    {
        if (view.IsPerspective || elements.Count is 0)
        {
            return;
        }

        // Видимость на виде считается по уже применённым шаблону и фильтру.
        document.Regenerate();

        var ids = elements.Select(element => element.Id).ToHashSet();
        var categories = elements.Select(element => element.Category.Id).Distinct().ToList();

        var visible = new FilteredElementCollector(document, view.Id)
            .WherePasses(new ElementMulticategoryFilter(categories))
            .WhereElementIsNotElementType()
            .Where(element => ids.Contains(element.Id))
            .ToList();

        var crop = view.CropBox;
        var toView = crop.Transform.Inverse;

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;

        foreach (var element in visible)
        {
            if (element.get_BoundingBox(null) is not { } box)
            {
                continue;
            }

            foreach (var corner in Corners(box))
            {
                var point = toView.OfPoint(corner);

                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
            }
        }

        if (minX > maxX)
        {
            return;
        }

        // Запас - под выноски и отметки, которые лягут вокруг схемы.
        var margin = Math.Max(
            UnitUtils.ConvertToInternalUnits(CropMarginMm, UnitTypeId.Millimeters),
            Math.Max(maxX - minX, maxY - minY) * CropMarginShare);

        view.CropBox = new BoundingBoxXYZ
        {
            Transform = crop.Transform,
            Min = new XYZ(minX - margin, minY - margin, crop.Min.Z),
            Max = new XYZ(maxX + margin, maxY + margin, crop.Max.Z)
        };

        // Обрезка выключена: кроме системы на схеме ничего нет, подрезать нечего. Рамка всё же
        // подогнана - включат «Обрезать вид», и она сразу окажется вокруг системы.
        view.CropBoxActive = false;
        view.CropBoxVisible = false;
    }

    private static IEnumerable<XYZ> Corners(BoundingBoxXYZ box)
    {
        foreach (var x in new[] { box.Min.X, box.Max.X })
        foreach (var y in new[] { box.Min.Y, box.Max.Y })
        foreach (var z in new[] { box.Min.Z, box.Max.Z })
        {
            yield return box.Transform.OfPoint(new XYZ(x, y, z));
        }
    }

    private static void FitSectionBox(View3D view, IReadOnlyList<Element> elements)
    {
        BoundingBoxXYZ? total = null;

        foreach (var element in elements)
        {
            if (element.get_BoundingBox(null) is not { } box)
            {
                continue;
            }

            if (total is null)
            {
                total = new BoundingBoxXYZ { Min = box.Min, Max = box.Max };
                continue;
            }

            total.Min = new XYZ(Math.Min(total.Min.X, box.Min.X), Math.Min(total.Min.Y, box.Min.Y), Math.Min(total.Min.Z, box.Min.Z));
            total.Max = new XYZ(Math.Max(total.Max.X, box.Max.X), Math.Max(total.Max.Y, box.Max.Y), Math.Max(total.Max.Z, box.Max.Z));
        }

        if (total is null)
        {
            return;
        }

        var margin = UnitUtils.ConvertToInternalUnits(SectionBoxMarginMm, UnitTypeId.Millimeters);
        var offset = new XYZ(margin, margin, margin);

        view.SetSectionBox(new BoundingBoxXYZ { Min = total.Min - offset, Max = total.Max + offset });
        view.IsSectionBoxActive = true;
    }
}
