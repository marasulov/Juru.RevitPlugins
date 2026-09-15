using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Schedules.Models;

using Juru.RevitPlugins.Schedules.Localization;

namespace Juru.RevitPlugins.Schedules.Services.Categories;

/// <summary>Названия листов. Номер, дата и подписи не переводятся — они не текст, а реквизиты.</summary>
public sealed class Sheets : TranslatableCategory
{
    public override string Key
    {
        get { return "Sheets"; }
    }

    public override string Name
    {
        get { return Text.CategorySheets; }
    }

    public override string EmptyNote
    {
        get { return Text.EmptySheets; }
    }

    public override IReadOnlyList<ElementText> Collect(CollectScope scope)
    {
        // Лист сам себе вид: областью сканирования его не ограничить.
        var sheets = scope.WholeModel()
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>();

        return Order(sheets.SelectMany(sheet => DescribeAll(
            sheet,
            sheet.SheetNumber,
            TextTarget.Name(Text.TargetSheetName))));
    }
}

/// <summary>
///     Названия видов. Шаблоны видов пропускаются: они не выпускаются, а их переименование сбивает
///     привычную сортировку в диспетчере проекта. Листы и спецификации у себя на вкладках.
/// </summary>
public sealed class Views : TranslatableCategory
{
    public override string Key
    {
        get { return "Views"; }
    }

    public override string Name
    {
        get { return Text.CategoryViews; }
    }

    public override string EmptyNote
    {
        get { return Text.EmptyViews; }
    }

    public override IReadOnlyList<ElementText> Collect(CollectScope scope)
    {
        var views = scope.WholeModel()
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(view => !view.IsTemplate && view is not ViewSheet and not ViewSchedule);

        return Order(views.SelectMany(view => DescribeAll(
            view,
            view.ViewType.ToString(),
            TextTarget.Name(Text.TargetViewName),
            TextTarget.Parameter(BuiltInParameter.VIEW_DESCRIPTION, Text.TargetDescription))));
    }
}

/// <summary>Текстовые примечания в видах: единственная категория, где переводится сам текст объекта.</summary>
public sealed class TextNotes : TranslatableCategory
{
    public override string Key
    {
        get { return "TextNotes"; }
    }

    public override string Name
    {
        get { return Text.CategoryTextNotes; }
    }

    public override string EmptyNote
    {
        get { return Text.EmptyTextNotes; }
    }

    public override IReadOnlyList<ElementText> Collect(CollectScope scope)
    {
        var notes = scope
            .Instances(typeof(TextNote))
            .Cast<TextNote>();

        return Order(notes.SelectMany(note => DescribeAll(
            note,
            scope.Document.GetElement(note.OwnerViewId)?.Name ?? Text.ContextOutsideView,
            TextTarget.NoteText())));
    }
}

/// <summary>Названия уровней. Имена уникальны, поэтому столкновение Ревит отклонит с ошибкой в отчёте.</summary>
public sealed class Levels : TranslatableCategory
{
    public override string Key
    {
        get { return "Levels"; }
    }

    public override string Name
    {
        get { return Text.CategoryLevels; }
    }

    public override string EmptyNote
    {
        get { return Text.EmptyLevels; }
    }

    public override IReadOnlyList<ElementText> Collect(CollectScope scope)
    {
        var levels = scope.WholeModel().OfClass(typeof(Level)).WhereElementIsNotElementType();

        return Order(levels.SelectMany(level => DescribeAll(level, Text.ContextLevel, TextTarget.Name())));
    }
}

/// <summary>Материалы: имя печатается в спецификациях и в составе конструкций.</summary>
public sealed class Materials : TranslatableCategory
{
    public override string Key
    {
        get { return "Materials"; }
    }

    public override string Name
    {
        get { return Text.CategoryMaterials; }
    }

    public override string EmptyNote
    {
        get { return Text.EmptyMaterials; }
    }

    public override IReadOnlyList<ElementText> Collect(CollectScope scope)
    {
        var materials = scope.WholeModel()
            .OfClass(typeof(Material))
            .Cast<Material>();

        return Order(materials.SelectMany(material => DescribeAll(
            material,
            material.MaterialClass ?? Text.ContextMaterial,
            TextTarget.Name(),
            TextTarget.Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION, Text.TargetDescription),
            TextTarget.Parameter(BuiltInParameter.ALL_MODEL_MARK, Text.TargetMark),
            TextTarget.Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, Text.TargetComments))));
    }
}

/// <summary>Ревизии: описание попадает в штамп и в таблицу изменений на листе.</summary>
public sealed class Revisions : TranslatableCategory
{
    public override string Key
    {
        get { return "Revisions"; }
    }

    public override string Name
    {
        get { return Text.CategoryRevisions; }
    }

    public override string EmptyNote
    {
        get { return Text.EmptyRevisions; }
    }

    public override IReadOnlyList<ElementText> Collect(CollectScope scope)
    {
        var revisions = scope.WholeModel()
            .OfCategory(BuiltInCategory.OST_Revisions)
            .Cast<Revision>();

        return Order(revisions.SelectMany(revision => DescribeAll(
            revision,
            revision.SequenceNumber.ToString(),
            TextTarget.RevisionField(RevisionTextField.Description),
            TextTarget.RevisionField(RevisionTextField.IssuedTo),
            TextTarget.RevisionField(RevisionTextField.IssuedBy))));
    }
}

/// <summary>
///     Надписи у размеров: замена значения, префикс, суффикс и подписи над и под размерной линией.
/// </summary>
/// <remarks>
///     У размера с несколькими сегментами свои надписи есть у каждого сегмента, и надписи самого
///     размера при этом не действуют — поэтому собирается либо то, либо другое.
/// </remarks>
public sealed class Dimensions : TranslatableCategory
{
    private static readonly DimensionTextPlace[] Places =
    [
        DimensionTextPlace.ValueOverride,
        DimensionTextPlace.Above,
        DimensionTextPlace.Below,
        DimensionTextPlace.Prefix,
        DimensionTextPlace.Suffix
    ];

    public override string Key
    {
        get { return "Dimensions"; }
    }

    public override string Name
    {
        get { return Text.CategoryDimensions; }
    }

    public override string EmptyNote
    {
        get { return Text.EmptyDimensions; }
    }

    public override IReadOnlyList<ElementText> Collect(CollectScope scope)
    {
        var dimensions = scope
            .Instances(typeof(Dimension))
            .Cast<Dimension>();

        return Order(dimensions.SelectMany(Collect));
    }

    private IEnumerable<ElementText> Collect(Dimension dimension)
    {
        var context = dimension.Document.GetElement(dimension.OwnerViewId)?.Name ?? Text.ContextOutsideView;
        var segments = dimension.Segments;

        if (segments is null || segments.Size <= 1)
        {
            return DescribeAll(
                dimension,
                context,
                Places.Select(place => TextTarget.DimensionText(place, null)).ToArray());
        }

        return Enumerable
            .Range(0, segments.Size)
            .SelectMany(index => DescribeAll(
                dimension,
                context,
                Places.Select(place => TextTarget.DimensionText(place, index)).ToArray()));
    }
}
