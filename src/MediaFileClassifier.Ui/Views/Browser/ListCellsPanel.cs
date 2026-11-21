using Avalonia;
using Avalonia.Controls;
using MediaFileClassifier.Ui.ViewModels.Browser;

namespace MediaFileClassifier.Ui.Views.Browser;

/// <summary>
/// Lays out one row of the list view (E14-S02), or its header: each child names its column with
/// <see cref="ColumnProperty"/>, and the children are placed side by side in the order and at the widths of the
/// <see cref="LayoutProperty"/> columns. Hidden columns' cells are hidden. The layout is inherited, so it's set once
/// on the list and every row follows it.
/// </summary>
public sealed class ListCellsPanel : Panel
{
    /// <summary>
    /// Defines the inherited <c>Layout</c> attached property: the columns to lay out by.
    /// </summary>
    public static readonly AttachedProperty<ListColumnsViewModel?> LayoutProperty =
        AvaloniaProperty.RegisterAttached<ListCellsPanel, Control, ListColumnsViewModel?>("Layout", inherits: true);

    /// <summary>
    /// Defines the <c>Column</c> attached property: the column a cell belongs to.
    /// </summary>
    public static readonly AttachedProperty<ListColumnKind> ColumnProperty =
        AvaloniaProperty.RegisterAttached<ListCellsPanel, Control, ListColumnKind>("Column");

    private ListColumnsViewModel? subscribed;

    static ListCellsPanel()
    {
        LayoutProperty.Changed.AddClassHandler<ListCellsPanel>((panel, _) => panel.Subscribe());
        AffectsParentMeasure<ListCellsPanel>(ColumnProperty);
    }

    /// <summary>
    /// Gets the columns a control lays out by.
    /// </summary>
    /// <param name="control">The control.</param>
    /// <returns>The columns, or <see langword="null"/>.</returns>
    public static ListColumnsViewModel? GetLayout(Control control) => control?.GetValue(LayoutProperty);

    /// <summary>
    /// Sets the columns a control and its descendants lay out by.
    /// </summary>
    /// <param name="control">The control.</param>
    /// <param name="value">The columns.</param>
    public static void SetLayout(Control control, ListColumnsViewModel? value) => control?.SetValue(LayoutProperty, value);

    /// <summary>
    /// Gets the column a cell belongs to.
    /// </summary>
    /// <param name="control">The cell.</param>
    /// <returns>The column.</returns>
    public static ListColumnKind GetColumn(Control control) => control?.GetValue(ColumnProperty) ?? default;

    /// <summary>
    /// Sets the column a cell belongs to.
    /// </summary>
    /// <param name="control">The cell.</param>
    /// <param name="value">The column.</param>
    public static void SetColumn(Control control, ListColumnKind value) => control?.SetValue(ColumnProperty, value);

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Subscribe();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Unsubscribe();
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var columns = GetLayout(this);
        if (columns is null)
        {
            return default;
        }

        var height = 0.0;
        var width = 0.0;
        foreach (var child in Children)
        {
            var column = Find(columns, GetColumn(child));
            child.IsVisible = column?.IsVisible ?? false;
            if (column is { IsVisible: true })
            {
                child.Measure(new Size(column.Width, availableSize.Height));
                height = Math.Max(height, child.DesiredSize.Height);
            }
        }

        foreach (var column in columns.Columns)
        {
            width += column.IsVisible ? column.Width : 0;
        }

        return new Size(width, height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = GetLayout(this);
        if (columns is null)
        {
            return finalSize;
        }

        var x = 0.0;
        foreach (var column in columns.Columns)
        {
            if (!column.IsVisible)
            {
                continue;
            }

            foreach (var child in Children)
            {
                if (GetColumn(child) == column.Kind)
                {
                    child.Arrange(new Rect(x, 0, column.Width, finalSize.Height));
                }
            }

            x += column.Width;
        }

        return finalSize;
    }

    private static ListColumnViewModel? Find(ListColumnsViewModel columns, ListColumnKind kind)
    {
        foreach (var column in columns.Columns)
        {
            if (column.Kind == kind)
            {
                return column;
            }
        }

        return null;
    }

    private void Subscribe()
    {
        var columns = GetLayout(this);
        if (ReferenceEquals(columns, subscribed) || VisualRoot is null)
        {
            InvalidateMeasure();
            return;
        }

        Unsubscribe();
        subscribed = columns;
        if (columns is not null)
        {
            columns.LayoutChanged += OnLayoutChanged;
        }

        InvalidateMeasure();
    }

    private void Unsubscribe()
    {
        if (subscribed is not null)
        {
            subscribed.LayoutChanged -= OnLayoutChanged;
            subscribed = null;
        }
    }

    private void OnLayoutChanged(object? sender, EventArgs e) => InvalidateMeasure();
}
