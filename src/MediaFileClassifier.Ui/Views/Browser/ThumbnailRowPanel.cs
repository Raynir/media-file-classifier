using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using MediaFileClassifier.Ui.ViewModels.Browser;

namespace MediaFileClassifier.Ui.Views.Browser;

/// <summary>
/// Lays out one row's tiles side by side (E14-S01-T01). Tile views are built once and kept: when the grid's panel
/// reuses the row for another one, only their data context changes. Profiling the grid at 100k items showed that
/// building tiles (templates, styles, attaching) took about half of the UI thread while scrolling.
/// </summary>
public sealed class ThumbnailRowPanel : StackPanel
{
    /// <summary>
    /// Defines the <see cref="Tiles"/> property.
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<ThumbnailTileViewModel>?> TilesProperty =
        AvaloniaProperty.Register<ThumbnailRowPanel, IReadOnlyList<ThumbnailTileViewModel>?>(nameof(Tiles));

    /// <summary>
    /// Defines the <see cref="TileTemplate"/> property.
    /// </summary>
    public static readonly StyledProperty<IDataTemplate?> TileTemplateProperty =
        AvaloniaProperty.Register<ThumbnailRowPanel, IDataTemplate?>(nameof(TileTemplate));

    static ThumbnailRowPanel()
    {
        TilesProperty.Changed.AddClassHandler<ThumbnailRowPanel>((panel, _) => panel.Update());

        // Switching between the grid and the list (E14-S02) swaps the template: the old views go.
        TileTemplateProperty.Changed.AddClassHandler<ThumbnailRowPanel>((panel, _) =>
        {
            panel.Children.Clear();
            panel.Update();
        });
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ThumbnailRowPanel"/> class.
    /// </summary>
    public ThumbnailRowPanel() => Orientation = Orientation.Horizontal;

    /// <summary>
    /// Gets or sets the row's tiles.
    /// </summary>
    public IReadOnlyList<ThumbnailTileViewModel>? Tiles
    {
        get => GetValue(TilesProperty);
        set => SetValue(TilesProperty, value);
    }

    /// <summary>
    /// Gets or sets the row the panel shows, so the rows control can detach it when the panel is recycled.
    /// </summary>
    public ThumbnailRowViewModel? Row { get; set; }

    /// <summary>
    /// Gets or sets the template a tile view is built from.
    /// </summary>
    public IDataTemplate? TileTemplate
    {
        get => GetValue(TileTemplateProperty);
        set => SetValue(TileTemplateProperty, value);
    }

    private void Update()
    {
        var tiles = Tiles ?? [];
        for (var index = 0; index < Children.Count; index++)
        {
            // Spare views (a shorter last row) stay built but hidden, for the next full row.
            Children[index].IsVisible = index < tiles.Count;
            if (index >= tiles.Count)
            {
                Children[index].DataContext = null;
            }
        }

        for (var index = 0; index < tiles.Count; index++)
        {
            if (index == Children.Count)
            {
                // The data context is set before the view joins the panel: otherwise its compiled bindings first
                // evaluate against the row view model the panel inherits, and log a cast warning per binding (MFC-1003).
                var view = TileTemplate?.Build(tiles[index]) ?? new Control();
                view.DataContext = tiles[index];
                Children.Add(view);
            }

            Children[index].DataContext = tiles[index];
        }
    }
}
