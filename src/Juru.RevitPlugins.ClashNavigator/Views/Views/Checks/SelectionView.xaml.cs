using System.Windows;
using System.Windows.Controls;

namespace Juru.RevitPlugins.ClashNavigator.Views.Checks;
/// <summary>
/// Какую часть выборки показывает <see cref="SelectionView" />.
/// </summary>
public enum SelectionPart {
    Both,
    Files,
    SearchSets
}

/// <summary>
/// Выборка проверки: файлы и поисковые наборы.
/// </summary>
/// <remarks>
/// В таблице проверок файлы и наборы стоят в отдельных колонках со своими заголовками, поэтому
/// одна выборка рисуется двумя экземплярами с разным <see cref="Part" />.
/// </remarks>
public partial class SelectionView : UserControl {
    public static readonly DependencyProperty PartProperty = DependencyProperty.Register(
        nameof(Part), typeof(SelectionPart), typeof(SelectionView),
        new PropertyMetadata(SelectionPart.Both, (d, _) => ((SelectionView) d).ApplyPart()));

    public SelectionView() {
        InitializeComponent();
        ApplyPart();
    }

    public SelectionPart Part {
        get => (SelectionPart) GetValue(PartProperty);
        set => SetValue(PartProperty, value);
    }

    private void ApplyPart() {
        bool files = Part != SelectionPart.SearchSets;
        bool searchSets = Part != SelectionPart.Files;
        FilesPart.Visibility = files ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        SearchSetsPart.Visibility = searchSets ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        FilesColumn.Width = files ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        SearchSetsColumn.Width = searchSets ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        FilesPart.Margin = searchSets ? new Thickness(0, 0, 4, 0) : new Thickness(0);
    }
}
