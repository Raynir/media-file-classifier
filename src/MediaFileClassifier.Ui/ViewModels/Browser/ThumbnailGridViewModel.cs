using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.UseCases.Browsing;
using MediaFileClassifier.UseCases.Browsing.ThumbnailTileSize;
using MediatR;

namespace MediaFileClassifier.Ui.ViewModels.Browser;

/// <summary>
/// The virtualised thumbnail grid of the media browser (E14-S01). Rows of tiles are virtualised by the view's
/// panel, results are paged by <see cref="ResultPageCache"/>, thumbnails come from the thumbnail cache through
/// <see cref="ThumbnailLoader"/>, and the items on screen without a thumbnail are boosted in the job system (D150).
/// Use it on the UI thread.
/// </summary>
public sealed partial class ThumbnailGridViewModel : ViewModelBase
{
    /// <summary>
    /// The smallest tile width, in device-independent pixels.
    /// </summary>
    public const int MinTileSize = 96;

    /// <summary>
    /// The largest tile width, in device-independent pixels.
    /// </summary>
    public const int MaxTileSize = 384;

    /// <summary>
    /// How far the slider, <c>=</c> / <c>-</c> and Ctrl+wheel change the tile width.
    /// </summary>
    public const int TileSizeStep = 32;

    /// <summary>
    /// The space between tiles, as in Figma (16).
    /// </summary>
    public const double Gap = 16;

    /// <summary>
    /// The height of the file name and details under the thumbnail, with the tile border.
    /// </summary>
    public const double InfoStripHeight = 40;

    private static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan BoostDelay = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

    private readonly IMediaResultSource source;
    private readonly ThumbnailLoader loader;
    private readonly IItemOpener opener;
    private readonly IJobSystem jobs;
    private readonly ISender sender;
    private readonly ResultPageCache pages;
    private readonly HashSet<ThumbnailRowViewModel> attached = [];
    private readonly DispatcherTimer refreshTimer;
    private readonly DispatcherTimer boostTimer;
    private readonly DispatcherTimer saveTimer;
    private HashSet<Id<MediaItem>> boosted = [];
    private double viewportWidth;
    private double viewportOffset;
    private double viewportHeight;
    private double renderScaling = 1;
    private bool isActive;
    private bool restoringSize;
    private bool refreshing;
    private bool refreshAgain;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThumbnailGridViewModel"/> class.
    /// </summary>
    /// <param name="source">The results to show.</param>
    /// <param name="loader">Loads thumbnails.</param>
    /// <param name="opener">Opens an item.</param>
    /// <param name="jobs">The job system, for the visible-first boost.</param>
    /// <param name="sender">Sends the tile size requests.</param>
    public ThumbnailGridViewModel(IMediaResultSource source, ThumbnailLoader loader, IItemOpener opener, IJobSystem jobs, ISender sender)
    {
        this.source = source;
        this.loader = loader;
        this.opener = opener;
        this.jobs = jobs;
        this.sender = sender;
        pages = new ResultPageCache(source);
        pages.PageLoaded += (_, page) => OnPageLoaded(page);
        Rows = new ThumbnailRowList(CreateRow);
        refreshTimer = new DispatcherTimer { Interval = RefreshDelay };
        refreshTimer.Tick += (_, _) =>
        {
            refreshTimer.Stop();
            _ = RefreshAsync();
        };
        boostTimer = new DispatcherTimer { Interval = BoostDelay };
        boostTimer.Tick += (_, _) =>
        {
            boostTimer.Stop();
            Boost();
        };
        saveTimer = new DispatcherTimer { Interval = SaveDelay };
        saveTimer.Tick += (_, _) =>
        {
            saveTimer.Stop();
            _ = SaveTileSizeAsync();
        };
        source.Changed += (_, _) => Dispatcher.UIThread.Post(OnSourceChanged);
    }

    /// <summary>
    /// Raised when the view should scroll, e.g. to keep the first visible item in place, with the new vertical offset.
    /// </summary>
    public event EventHandler<double>? ScrollRequested;

    /// <summary>
    /// Gets the rows.
    /// </summary>
    public ThumbnailRowList Rows { get; }

    /// <summary>
    /// Gets the number of results.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial int Count { get; private set; }

    /// <summary>
    /// Gets a value indicating whether there's nothing to show.
    /// </summary>
    public bool IsEmpty => Count == 0;

    /// <summary>
    /// Gets or sets the tile width, in device-independent pixels. It snaps to <see cref="TileSizeStep"/> within
    /// <see cref="MinTileSize"/>–<see cref="MaxTileSize"/>, and is remembered per library (D050).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImageHeight), nameof(RowHeight))]
    public partial int TileSize { get; set; } = LibrarySettings.DefaultThumbnailTileSize;

    /// <summary>
    /// Gets the height of a tile's thumbnail area: 4:3 under the tile width.
    /// </summary>
    public double ImageHeight => Math.Round(TileSize * 3 / 4.0);

    /// <summary>
    /// Gets the height of a row, with the gap under it.
    /// </summary>
    public double RowHeight => ImageHeight + InfoStripHeight + Gap;

    /// <summary>
    /// Gets the number of tiles per row.
    /// </summary>
    public int Columns { get; private set; } = 1;

    /// <summary>
    /// Gets the stored thumbnail size the tiles load at this tile width and screen scaling.
    /// </summary>
    public ThumbnailSize ThumbnailSize { get; private set; } = ThumbnailSize.Small;

    /// <summary>
    /// Gets or sets how to find the tile with keyboard focus, which Enter opens. The view sets it; asking when needed
    /// stays right after the focused tile view was reused for another item.
    /// </summary>
    public Func<ThumbnailTileViewModel?>? FocusedTile { get; set; }

    /// <summary>
    /// Gets the result pages, for tests and measurements.
    /// </summary>
    internal ResultPageCache Pages => pages;

    /// <summary>
    /// Snaps a tile width to the size range and steps.
    /// </summary>
    /// <param name="size">The width.</param>
    /// <returns>The nearest allowed width.</returns>
    public static int Snap(double size)
    {
        var steps = Math.Round((size - MinTileSize) / TileSizeStep, MidpointRounding.AwayFromZero);
        return (int)Math.Clamp(MinTileSize + (steps * TileSizeStep), MinTileSize, MaxTileSize);
    }

    /// <summary>
    /// Starts showing the open library: reads its tile size and the result count.
    /// </summary>
    /// <returns>A task that completes when the first count is known.</returns>
    public async Task ActivateAsync()
    {
        isActive = true;
        var size = await sender.Send(new GetThumbnailTileSizeQuery());
        restoringSize = true;
        TileSize = Snap(size ?? LibrarySettings.DefaultThumbnailTileSize);
        restoringSize = false;
        await RefreshAsync(resetScroll: true);
    }

    /// <summary>
    /// Stops showing results, e.g. when the library closes: the boost is cleared and a pending size change saved.
    /// </summary>
    public void Deactivate()
    {
        if (!isActive)
        {
            return;
        }

        isActive = false;
        refreshTimer.Stop();
        boostTimer.Stop();
        if (saveTimer.IsEnabled)
        {
            saveTimer.Stop();
            _ = SaveTileSizeAsync();
        }

        jobs.Current?.Boost([]);
        boosted = [];
        pages.Invalidate(0);
        Count = 0;
        Rows.Reset(0);
    }

    /// <summary>
    /// Tells the grid what the view shows, so it can lay out the columns and load what's on screen.
    /// </summary>
    /// <param name="width">The width tiles can use.</param>
    /// <param name="offset">The vertical scroll offset.</param>
    /// <param name="height">The viewport height.</param>
    /// <param name="scaling">The screen's render scaling.</param>
    public void UpdateViewport(double width, double offset, double height, double scaling)
    {
        var anchor = FirstVisibleIndex();
        viewportWidth = width;
        viewportOffset = offset;
        viewportHeight = height;
        renderScaling = scaling > 0 ? scaling : 1;
        if (!Relayout(anchor))
        {
            RequestVisiblePages();
            boostTimer.Stop();
            boostTimer.Start();
        }
    }

    /// <summary>
    /// Opens a tile's item, e.g. on double-click.
    /// </summary>
    /// <param name="tile">The tile, or <see langword="null"/> for the focused one.</param>
    /// <returns>A task that completes when the item opened.</returns>
    [RelayCommand]
    public async Task OpenAsync(ThumbnailTileViewModel? tile)
    {
        if ((tile ?? FocusedTile?.Invoke())?.Item is { } item)
        {
            await opener.OpenAsync(item.Id);
        }
    }

    /// <summary>
    /// Makes the tiles one step larger.
    /// </summary>
    [RelayCommand]
    public void Larger() => TileSize = Snap(TileSize + TileSizeStep);

    /// <summary>
    /// Makes the tiles one step smaller.
    /// </summary>
    [RelayCommand]
    public void Smaller() => TileSize = Snap(TileSize - TileSizeStep);

    /// <summary>
    /// Runs the delayed work now instead of waiting for its timer: a pending refresh, boost and size save. For tests.
    /// </summary>
    /// <returns>A task that completes when the work is done.</returns>
    internal async Task RunPendingWorkAsync()
    {
        if (refreshTimer.IsEnabled)
        {
            refreshTimer.Stop();
            await RefreshAsync();
        }

        boostTimer.Stop();
        Boost();
        if (saveTimer.IsEnabled)
        {
            saveTimer.Stop();
            await SaveTileSizeAsync();
        }
    }

    /// <summary>
    /// Called by a row when its view starts showing it.
    /// </summary>
    /// <param name="row">The row.</param>
    internal void OnRowAttached(ThumbnailRowViewModel row)
    {
        attached.Add(row);
        ShowRow(row);
        if (row.Tiles.Count > 0)
        {
            pages.Request(row.Index * Columns, (row.Index * Columns) + row.Tiles.Count - 1);
        }

        boostTimer.Stop();
        boostTimer.Start();
    }

    /// <summary>
    /// Called by a row when its view stops showing it.
    /// </summary>
    /// <param name="row">The row.</param>
    internal void OnRowDetached(ThumbnailRowViewModel row)
    {
        attached.Remove(row);
        foreach (var tile in row.Tiles)
        {
            tile.Clear();
        }
    }

    partial void OnTileSizeChanged(int oldValue, int newValue)
    {
        var snapped = Snap(newValue);
        if (snapped != newValue)
        {
            TileSize = snapped;
            return;
        }

        // The slider sets TileSize directly; keep the first visible item in place and remember the size.
        var anchor = oldValue > 0 && Columns > 0 && viewportOffset > 0
            ? (int)Math.Floor(viewportOffset / (Math.Round(oldValue * 3 / 4.0) + InfoStripHeight + Gap)) * Columns
            : 0;
        Relayout(anchor, force: true);
        if (isActive && !restoringSize)
        {
            saveTimer.Stop();
            saveTimer.Start();
        }
    }

    private ThumbnailRowViewModel CreateRow(int index)
    {
        var first = index * Columns;
        var tiles = new ThumbnailTileViewModel[Math.Max(0, Math.Min(Columns, Count - first))];
        for (var column = 0; column < tiles.Length; column++)
        {
            tiles[column] = new ThumbnailTileViewModel(loader, TileSize, ImageHeight);
        }

        return new ThumbnailRowViewModel(this, index, tiles);
    }

    private void ShowRow(ThumbnailRowViewModel row)
    {
        var first = row.Index * Columns;
        for (var column = 0; column < row.Tiles.Count; column++)
        {
            row.Tiles[column].Show(first + column, pages.Get(first + column), ThumbnailSize);
        }
    }

    private int FirstVisibleIndex() => viewportOffset <= 0 ? 0 : (int)Math.Floor(viewportOffset / RowHeight) * Columns;

    /// <summary>
    /// Recomputes the columns and the thumbnail size, and rebuilds the rows if either changed.
    /// </summary>
    /// <returns>Whether the rows were rebuilt.</returns>
    private bool Relayout(int anchorIndex, bool force = false)
    {
        var columns = Math.Max(1, (int)Math.Floor((viewportWidth + Gap) / (TileSize + Gap)));
        var size = ThumbnailLoader.SizeFor(TileSize, renderScaling);
        if (!force && columns == Columns && size == ThumbnailSize)
        {
            return false;
        }

        Columns = columns;
        ThumbnailSize = size;
        Rows.Reset(RowCount());
        if (anchorIndex > 0)
        {
            var offset = anchorIndex / Columns * RowHeight;
            viewportOffset = offset;
            ScrollRequested?.Invoke(this, offset);
        }

        RequestVisiblePages();
        return true;
    }

    private int RowCount() => (Count + Columns - 1) / Columns;

    private (int First, int Last) VisibleRows()
    {
        var first = Math.Max(0, (int)Math.Floor(viewportOffset / RowHeight));
        var last = (int)Math.Floor((viewportOffset + Math.Max(viewportHeight, RowHeight)) / RowHeight);
        return (first, Math.Min(last, RowCount() - 1));
    }

    private void RequestVisiblePages()
    {
        if (Count == 0)
        {
            return;
        }

        // The screen, and one screen ahead and behind, so scrolling rarely waits for a page.
        var (first, last) = VisibleRows();
        var screen = Math.Max(1, last - first + 1);
        pages.Request(Math.Max(0, first - screen) * Columns, Math.Min(Count - 1, ((last + screen + 1) * Columns) - 1));
    }

    private void OnPageLoaded(int page)
    {
        var first = page * ResultPageCache.PageSize;
        var last = first + ResultPageCache.PageSize - 1;
        foreach (var row in attached)
        {
            var rowFirst = row.Index * Columns;
            if (rowFirst <= last && rowFirst + Columns - 1 >= first)
            {
                ShowRow(row);
            }
        }

        boostTimer.Stop();
        boostTimer.Start();
    }

    private void OnSourceChanged()
    {
        if (isActive && !refreshTimer.IsEnabled)
        {
            refreshTimer.Start();
        }
    }

    private async Task RefreshAsync(bool resetScroll = false)
    {
        if (refreshing)
        {
            refreshAgain = true;
            return;
        }

        refreshing = true;
        try
        {
            do
            {
                refreshAgain = false;
                await RefreshOnceAsync(resetScroll);
                resetScroll = false;
            }
            while (refreshAgain && isActive);
        }
        finally
        {
            refreshing = false;
        }
    }

    private async Task RefreshOnceAsync(bool resetScroll)
    {
        var anchorIndex = resetScroll ? 0 : FirstVisibleIndex();
        var anchor = anchorIndex > 0 ? pages.Get(anchorIndex) : null;
        var count = await source.CountAsync(CancellationToken.None);
        if (!isActive)
        {
            return;
        }

        // Keep the first visible item in place when results change above it. At the top, new items show.
        var shift = count - Count;
        var newAnchor = anchorIndex;
        if (anchor is not null)
        {
            newAnchor = await source.PositionOfAsync(anchor.Id, CancellationToken.None);
            shift = newAnchor - anchorIndex;
        }

        pages.Invalidate(resetScroll ? 0 : shift);
        if (count != Count || resetScroll)
        {
            Count = count;
            var withinRow = resetScroll ? 0 : viewportOffset - (Math.Floor(viewportOffset / RowHeight) * RowHeight);
            Rows.Reset(RowCount());
            var offset = resetScroll ? 0 : (newAnchor / Columns * RowHeight) + (anchorIndex > 0 ? withinRow : 0);
            if (resetScroll || anchor is not null)
            {
                viewportOffset = offset;
                ScrollRequested?.Invoke(this, offset);
            }
        }

        RequestVisiblePages();
    }

    private void Boost()
    {
        if (!isActive || jobs.Current is not { } scheduler)
        {
            return;
        }

        var (first, last) = VisibleRows();
        var wanted = attached
            .Where(row => row.Index >= first && row.Index <= last)
            .SelectMany(row => row.Tiles)
            .Where(tile => tile.NeedsThumbnail)
            .Select(tile => tile.Item!.Id)
            .ToHashSet();
        if (!wanted.SetEquals(boosted))
        {
            boosted = wanted;
            scheduler.Boost(wanted);
        }
    }

    private async Task SaveTileSizeAsync()
    {
        try
        {
            await sender.Send(new SetThumbnailTileSizeCommand(TileSize));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The size is only a convenience; the next change tries again.
        }
    }
}
