namespace MediaFileClassifier.Ui.ViewModels.Browser;

/// <summary>
/// One row of the thumbnail grid (E14-S01-T01). The grid virtualises rows of tiles with Avalonia's own
/// <c>VirtualizingStackPanel</c>; the rows control calls <see cref="Attach"/> and <see cref="Detach"/> as the panel
/// realises and recycles it, so only rows on screen load pages and thumbnails.
/// </summary>
public sealed class ThumbnailRowViewModel
{
    private readonly ThumbnailGridViewModel grid;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThumbnailRowViewModel"/> class.
    /// </summary>
    /// <param name="grid">The grid.</param>
    /// <param name="index">The row number.</param>
    /// <param name="tiles">The row's tiles, one per result.</param>
    internal ThumbnailRowViewModel(ThumbnailGridViewModel grid, int index, IReadOnlyList<ThumbnailTileViewModel> tiles)
    {
        this.grid = grid;
        Index = index;
        Tiles = tiles;
    }

    /// <summary>
    /// Gets the row number.
    /// </summary>
    public int Index { get; }

    /// <summary>
    /// Gets the tiles.
    /// </summary>
    public IReadOnlyList<ThumbnailTileViewModel> Tiles { get; }

    /// <summary>
    /// Gets a value indicating whether a row container shows the row.
    /// </summary>
    public bool IsAttached { get; private set; }

    /// <summary>
    /// Starts showing the row: its tiles show their results and load their thumbnails.
    /// </summary>
    public void Attach()
    {
        if (!IsAttached)
        {
            IsAttached = true;
            grid.OnRowAttached(this);
        }
    }

    /// <summary>
    /// Stops showing the row: loads are cancelled and thumbnails let go.
    /// </summary>
    public void Detach()
    {
        if (IsAttached)
        {
            IsAttached = false;
            grid.OnRowDetached(this);
        }
    }
}
