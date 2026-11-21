using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.ViewModels.Browser;

namespace MediaFileClassifier.Ui.Views.Browser;

/// <summary>
/// The list view's column header (E14-S02): drag a cell's right edge to resize its column, drag a cell to move its
/// column, right-click to show or hide columns or reset them (E14-S02-T02).
/// </summary>
public partial class ListHeaderView : UserControl
{
    private const double DragThreshold = 4;

    private ListColumnsViewModel? columns;
    private ListColumnViewModel? pressed;
    private Border? pressedCell;
    private Point pressedAt;
    private bool dragging;

    /// <summary>
    /// Initializes a new instance of the <see cref="ListHeaderView"/> class.
    /// </summary>
    public ListHeaderView()
    {
        InitializeComponent();
        AddHandler(Thumb.DragDeltaEvent, OnResize);
        Cells.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        Cells.AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
        Cells.AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel);
        Cells.AddHandler(PointerCaptureLostEvent, (_, _) => EndDrag(), RoutingStrategies.Tunnel);
        Cells.AddHandler(ContextRequestedEvent, OnContextRequested, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Builds the show and hide menu: a check item per column that can be hidden, then Reset columns.
    /// </summary>
    /// <param name="columns">The columns.</param>
    /// <returns>The menu items.</returns>
    internal static List<Control> BuildMenu(ListColumnsViewModel columns)
    {
        ArgumentNullException.ThrowIfNull(columns);

        var items = new List<Control>();
        foreach (var column in columns.Columns.Where(column => column.CanHide).OrderBy(column => column.Kind))
        {
            var kind = column.Kind;
            var item = new MenuItem
            {
                Header = column.Name,
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = column.IsVisible,
            };
            item.Click += (_, _) => columns.SetVisible(kind, !columns[kind].IsVisible);
            items.Add(item);
        }

        items.Add(new Separator());
        items.Add(new MenuItem { Header = Localizer.Current["List_Columns_Reset"], Command = columns.ResetCommand });
        return items;
    }

    /// <summary>
    /// Finds the column at a point of the header, as laid out now.
    /// </summary>
    /// <param name="columns">The columns.</param>
    /// <param name="x">The distance from the header's left edge.</param>
    /// <returns>The column, or the last visible one past the end.</returns>
    internal static ListColumnViewModel? ColumnAt(ListColumnsViewModel columns, double x)
    {
        ArgumentNullException.ThrowIfNull(columns);

        var left = 0.0;
        ListColumnViewModel? last = null;
        foreach (var column in columns.Columns.Where(column => column.IsVisible))
        {
            if (x < left + column.Width)
            {
                return column;
            }

            left += column.Width;
            last = column;
        }

        return last;
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (columns is not null)
        {
            columns.Columns.CollectionChanged -= OnColumnsChanged;
        }

        columns = DataContext as ListColumnsViewModel;
        if (columns is not null)
        {
            columns.Columns.CollectionChanged += OnColumnsChanged;
        }

        BuildCells();
    }

    private static ListColumnViewModel? ColumnOf(object? source) =>
        (source as Visual)?.GetSelfAndVisualAncestors().OfType<StyledElement>().Select(element => element.DataContext).OfType<ListColumnViewModel>().FirstOrDefault();

    private void OnColumnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // A move only changes the order, which the panel reads from the columns; a reset brings new columns.
        if (e.Action != NotifyCollectionChangedAction.Move)
        {
            BuildCells();
        }
    }

    private void BuildCells()
    {
        Cells.Children.Clear();
        if (columns is null || Resources["HeaderCellTemplate"] is not IDataTemplate template)
        {
            return;
        }

        foreach (var column in columns.Columns)
        {
            // The data context is set before the cell joins the panel, so its compiled bindings never see the list.
            var cell = template.Build(column) ?? new Control();
            cell.DataContext = column;
            ListCellsPanel.SetColumn(cell, column.Kind);
            Cells.Children.Add(cell);
        }
    }

    private void OnResize(object? sender, VectorEventArgs e)
    {
        if (columns is not null && (e.Source as Control)?.DataContext is ListColumnViewModel column)
        {
            columns.Resize(column.Kind, column.Width + e.Vector.X);
            e.Handled = true;
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(Cells);
        if (!point.Properties.IsLeftButtonPressed || (e.Source as Visual)?.FindAncestorOfType<Thumb>(includeSelf: true) is not null)
        {
            return;
        }

        pressed = ColumnOf(e.Source);
        pressedCell = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<Border>().FirstOrDefault(border => border.Classes.Contains("ListHeaderCell"));
        pressedAt = point.Position;
        dragging = false;
        if (pressed is not null)
        {
            e.Pointer.Capture(Cells);
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (pressed is not { IsConfigurable: true })
        {
            return;
        }

        var position = e.GetPosition(Cells);
        if (!dragging && Math.Abs(position.X - pressedAt.X) > DragThreshold)
        {
            dragging = true;
            pressedCell?.Classes.Add("Dragging");
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var column = pressed;
        var moved = dragging;
        var x = e.GetPosition(Cells).X;
        e.Pointer.Capture(null);
        EndDrag();
        if (columns is null || column is null)
        {
            return;
        }

        if (moved)
        {
            // Dropped on a column: take its place. The thumbnail stays first.
            if (ColumnAt(columns, x) is { } target)
            {
                columns.Move(column.Kind, columns.Columns.IndexOf(target));
            }

            e.Handled = true;
        }
    }

    private void EndDrag()
    {
        pressedCell?.Classes.Remove("Dragging");
        pressedCell = null;
        pressed = null;
        dragging = false;
    }

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        ColumnsMenu.ItemsSource = columns is null ? [] : BuildMenu(columns);
        e.Handled = columns is null;
    }
}
