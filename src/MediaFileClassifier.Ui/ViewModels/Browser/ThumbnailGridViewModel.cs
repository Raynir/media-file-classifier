using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Search;
using MediaFileClassifier.Ui.Search.Filters;
using MediaFileClassifier.UseCases.Browsing;
using MediaFileClassifier.UseCases.Browsing.ThumbnailTileSize;
using MediaFileClassifier.UseCases.Favorites.ToggleFavorite;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Ui.ViewModels.Browser;

/// <summary>
/// The virtualised thumbnail grid of the media browser (E14-S01). Rows of tiles are virtualised by the view's
/// panel, results are paged by <see cref="ResultPageCache"/>, thumbnails come from the thumbnail cache through
/// <see cref="ThumbnailLoader"/>, and the items on screen without a thumbnail are boosted in the job system (D150).
/// Clicks and keys change the shared <see cref="SelectionModel"/> (E14-S03). Use it on the UI thread.
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
    private readonly IResultAnnotations? annotations;
    private readonly ThumbnailLoader loader;
    private readonly IItemOpener opener;
    private readonly IJobSystem jobs;
    private readonly ISender sender;
    private readonly ILogger logger;
    private readonly ResultPageCache pages;
    private readonly SelectionModel selection;
    private readonly BrowserContextMenu? contextMenu;
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
    /// <param name="selection">The selection.</param>
    /// <param name="contextMenu">Builds the context menu, or <see langword="null"/> for none.</param>
    /// <param name="notice">The notice above the results while a search is on, or <see langword="null"/> for none.</param>
    /// <param name="filters">The filter chips and panel above the results, or <see langword="null"/> for none.</param>
    /// <param name="sort">The sort control above the results, or <see langword="null"/> for none.</param>
    /// <param name="logger">Logs a favorite mark that could not be stored.</param>
    public ThumbnailGridViewModel(
        IMediaResultSource source,
        ThumbnailLoader loader,
        IItemOpener opener,
        IJobSystem jobs,
        ISender sender,
        SelectionModel selection,
        BrowserContextMenu? contextMenu = null,
        SearchNoticeViewModel? notice = null,
        FilterBarViewModel? filters = null,
        SortBarViewModel? sort = null,
        ILogger<ThumbnailGridViewModel>? logger = null)
    {
        this.logger = logger ?? NullLogger<ThumbnailGridViewModel>.Instance;
        Notice = notice;
        Filters = filters;
        Sort = sort;
        this.source = source;
        annotations = source as IResultAnnotations;
        this.loader = loader;
        this.opener = opener;
        this.jobs = jobs;
        this.sender = sender;
        pages = new ResultPageCache(source);
        pages.PageLoaded += (_, page) => OnPageLoaded(page);
        this.selection = selection;
        this.contextMenu = contextMenu;
        selection.KnownId = index => pages.Get(index)?.Id;
        selection.Changed += (_, _) => UpdateTileStates();
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
    /// Raised when the grid should take keyboard focus, e.g. Esc in the details panel (E14-S05).
    /// </summary>
    public event EventHandler? FocusRequested;

    /// <summary>
    /// Gets the rows.
    /// </summary>
    public ThumbnailRowList Rows { get; }

    /// <summary>
    /// Gets the notice bar above the results while a search is on (E09-S01-T05), or <see langword="null"/>.
    /// </summary>
    public SearchNoticeViewModel? Notice { get; }

    /// <summary>
    /// Gets the filter chips and panel above the results (E10-S02), or <see langword="null"/>.
    /// </summary>
    public FilterBarViewModel? Filters { get; }

    /// <summary>
    /// Gets the sort control at the end of the results header (E10-S04-T02), or <see langword="null"/>.
    /// </summary>
    public SortBarViewModel? Sort { get; }

    /// <summary>
    /// Gets the selection.
    /// </summary>
    public SelectionModel Selection => selection;

    /// <summary>
    /// Gets or sets a value indicating whether the focused tile shows its focus border: after keyboard navigation,
    /// until the next click (E14-S03-T02).
    /// </summary>
    [ObservableProperty]
    public partial bool IsFocusVisible { get; set; }

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
        selection.Reset(0);
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
        if ((tile?.Item?.Id ?? selection.FocusedId) is { } id)
        {
            await opener.OpenAsync(id);
        }
    }

    /// <summary>
    /// Selects with the mouse (E14-S03-T02): a click, Ctrl+click or Shift+click on a tile.
    /// </summary>
    /// <param name="index">The tile's result position.</param>
    /// <param name="gesture">What the click does.</param>
    /// <returns>A task that completes when the selection changed.</returns>
    public Task ClickAsync(int index, SelectionGesture gesture)
    {
        IsFocusVisible = false;
        return selection.ApplyAsync(index, gesture);
    }

    /// <summary>
    /// Moves the focus with the keyboard (E14-S03-T02), as in Windows Explorer: the focused item is selected; with
    /// Shift the selection runs from the anchor, and with Ctrl only the focus moves (Ctrl+Shift adds the range).
    /// The grid scrolls to keep the focused tile on screen.
    /// </summary>
    /// <param name="move">Where to move.</param>
    /// <param name="extend">Whether Shift is down.</param>
    /// <param name="keepSelection">Whether Ctrl is down.</param>
    /// <returns>A task that completes when the selection changed.</returns>
    public async Task MoveAsync(GridMove move, bool extend, bool keepSelection)
    {
        if (Count == 0)
        {
            return;
        }

        var current = selection.FocusIndex;
        var page = Math.Max(1, (int)Math.Floor(viewportHeight / RowHeight)) * Columns;
        var target = current < 0 ? 0 : move switch
        {
            GridMove.Left => current - 1,
            GridMove.Right => current + 1,
            GridMove.Up => current - Columns,
            GridMove.Down => current + Columns,
            GridMove.PageUp => current - page,
            GridMove.PageDown => current + page,
            GridMove.Home => 0,
            _ => Count - 1,
        };
        if (move is GridMove.Up or GridMove.Down && (target < 0 || target >= Count))
        {
            // Up from the first row or down from the last stays put, except down into a shorter last row.
            target = move == GridMove.Down && current / Columns < RowCount() - 1 ? Count - 1 : current;
        }

        target = Math.Clamp(target, 0, Count - 1);
        IsFocusVisible = true;
        var gesture = extend
            ? keepSelection ? SelectionGesture.AddRange : SelectionGesture.Range
            : keepSelection ? SelectionGesture.FocusOnly : SelectionGesture.Replace;
        BringIntoView(target);
        await selection.ApplyAsync(target, gesture);
    }

    /// <summary>
    /// Selects the focused item with Space, or toggles it with Ctrl+Space.
    /// </summary>
    /// <param name="toggle">Whether Ctrl is down.</param>
    /// <returns>A task that completes when the selection changed.</returns>
    public Task SelectFocusedAsync(bool toggle)
    {
        IsFocusVisible = true;
        var index = Math.Max(0, selection.FocusIndex);
        BringIntoView(index);
        return selection.ApplyAsync(index, toggle ? SelectionGesture.Toggle : SelectionGesture.Replace);
    }

    /// <summary>
    /// Builds the context menu for the selection (E14-S04).
    /// </summary>
    /// <returns>The entries; empty when nothing applies.</returns>
    public IReadOnlyList<IMenuEntry> BuildContextMenu() => contextMenu?.Build() ?? [];

    /// <summary>
    /// Selects all results, without loading them (E14-S03-T01).
    /// </summary>
    /// <returns>A task that completes when the selection changed.</returns>
    public Task SelectAllAsync() => selection.SelectAllAsync();

    /// <summary>
    /// Asks the view to give the grid keyboard focus, showing the focused tile.
    /// </summary>
    public void RequestFocus()
    {
        IsFocusVisible = true;
        FocusRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Deselects everything.
    /// </summary>
    public void ClearSelection() => selection.Clear();

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
            var tile = row.Tiles[column];
            var item = pages.Get(first + column);
            tile.Show(first + column, item, ThumbnailSize, item is null ? null : annotations?.GetMatch(item.Id));
            UpdateTileState(tile);
        }
    }

    private void UpdateTileStates()
    {
        foreach (var row in attached)
        {
            foreach (var tile in row.Tiles)
            {
                UpdateTileState(tile);
            }
        }
    }

    private void UpdateTileState(ThumbnailTileViewModel tile)
    {
        tile.IsSelected = tile.Item is { } item && selection.IsSelected(item.Id);
        tile.IsFocused = IsFocusVisible && tile.Index == selection.FocusIndex;
    }

    partial void OnIsFocusVisibleChanged(bool value) => UpdateTileStates();

    private void BringIntoView(int index)
    {
        var top = index / Columns * RowHeight;
        var offset = viewportOffset;
        if (top < viewportOffset)
        {
            offset = top;
        }
        else if (top + RowHeight > viewportOffset + viewportHeight)
        {
            offset = Math.Max(0, top + RowHeight - viewportHeight);
        }

        if (offset != viewportOffset)
        {
            viewportOffset = offset;
            ScrollRequested?.Invoke(this, offset);
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

    /// <summary>
    /// Reloads the results now instead of after the usual delay, e.g. when a favorite changed (E11-S01), so every
    /// tile updates at once.
    /// </summary>
    /// <returns>A task that completes when the results are reloaded.</returns>
    public Task RefreshNowAsync()
    {
        refreshTimer.Stop();
        return isActive ? RefreshAsync() : Task.CompletedTask;
    }

    /// <summary>
    /// Toggles the favorite mark of the tile's item, from the heart on the tile (E11-S01).
    /// </summary>
    /// <param name="tile">The tile.</param>
    /// <returns>A task that completes when the mark is stored.</returns>
    public async Task ToggleFavoriteAsync(ThumbnailTileViewModel tile)
    {
        ArgumentNullException.ThrowIfNull(tile);

        if (tile.Item is not { } item)
        {
            return;
        }

        try
        {
            await sender.Send(new ToggleFavoriteCommand([item.Id]));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The click ran without a caller to tell; the heart keeps its state and the next refresh shows the truth.
            LogFavoriteFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't change the favorite mark of a tile's item")]
    private static partial void LogFavoriteFailed(ILogger logger, Exception exception);

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
        var moved = count != Count || shift != 0;
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

        // The selection keeps the items still in the results, and its focus follows its item. A refresh that only
        // changed item states, e.g. thumbnails arriving, leaves it alone.
        if (resetScroll)
        {
            selection.Reset(count);
        }
        else if (moved)
        {
            await selection.RefreshAsync(count);
        }
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
