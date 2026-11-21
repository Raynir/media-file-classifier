using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Search;
using MediaFileClassifier.UseCases.Browsing;

namespace MediaFileClassifier.Ui.ViewModels.Browser;

/// <summary>
/// One tile of the thumbnail grid (E14-S01-T04): an item's thumbnail, file name, type and size, status badges and
/// video duration or animated marker. Tiles are reused as the grid scrolls; <see cref="Show"/> points one at another
/// item, and a thumbnail that arrives for the previous item is dropped, so a tile never shows the wrong image.
/// </summary>
public sealed partial class ThumbnailTileViewModel : ObservableObject
{
    private readonly ThumbnailLoader loader;
    private CancellationTokenSource? loading;
    private ThumbnailLease? lease;
    private ThumbnailSize thumbnailSize;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThumbnailTileViewModel"/> class.
    /// </summary>
    /// <param name="loader">Loads thumbnails.</param>
    /// <param name="width">Tile width.</param>
    /// <param name="imageHeight">Height of the thumbnail area.</param>
    public ThumbnailTileViewModel(ThumbnailLoader loader, double width, double imageHeight)
    {
        this.loader = loader;
        Width = width;
        ImageHeight = imageHeight;
    }

    /// <summary>
    /// Gets the tile width.
    /// </summary>
    public double Width { get; }

    /// <summary>
    /// Gets the height of the thumbnail area.
    /// </summary>
    public double ImageHeight { get; }

    /// <summary>
    /// Gets the result position the tile shows.
    /// </summary>
    [ObservableProperty]
    public partial int Index { get; private set; } = -1;

    /// <summary>
    /// Gets the item, or <see langword="null"/> while its page loads.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(IsLoading),
        nameof(FileName),
        nameof(Details),
        nameof(MediaType),
        nameof(IsThumbnailFailed),
        nameof(IsFavorite),
        nameof(Badges),
        nameof(DurationText),
        nameof(IsAnimated),
        nameof(TypeText),
        nameof(DateTakenText),
        nameof(SizeText),
        nameof(ResolutionText),
        nameof(LengthText),
        nameof(StatusText),
        nameof(Folder),
        nameof(IsUnavailable))]
    public partial MediaItemSummary? Item { get; private set; }

    /// <summary>
    /// Gets why the item is in the search results, or <see langword="null"/> while browsing.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MatchReason), nameof(IsLessRelevant), nameof(IsSource))]
    public partial ResultMatch? Match { get; private set; }

    /// <summary>
    /// Gets the tooltip that says why the item matched, e.g. <c>Matched in: Tags</c>, or <see langword="null"/>.
    /// </summary>
    public string? MatchReason => MatchReasonText.For(Match, Localizer.Current);

    /// <summary>
    /// Gets a value indicating whether the item is a less relevant search result, which shows dimmed.
    /// </summary>
    public bool IsLessRelevant => Match?.IsLessRelevant ?? false;

    /// <summary>
    /// Gets a value indicating whether the tile is the item a "find similar" search started from, marked Source (D053).
    /// </summary>
    public bool IsSource => Match?.IsSource ?? false;

    /// <summary>
    /// Gets the thumbnail, or <see langword="null"/> while it loads or when there's none, so the cell shows its
    /// placeholder (D126).
    /// </summary>
    [ObservableProperty]
    public partial IImage? Thumbnail { get; private set; }

    /// <summary>
    /// Gets or sets a value indicating whether the item is selected (E14-S03).
    /// </summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the tile shows the keyboard focus (E14-S03).
    /// </summary>
    [ObservableProperty]
    public partial bool IsFocused { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the file name shows under the thumbnail (the Show file names setting).
    /// </summary>
    [ObservableProperty]
    public partial bool ShowFileName { get; set; } = true;

    /// <summary>
    /// Gets a value indicating whether the item's page is still loading.
    /// </summary>
    public bool IsLoading => Item is null;

    /// <summary>
    /// Gets the file name.
    /// </summary>
    public string FileName => Item?.FileName ?? string.Empty;

    /// <summary>
    /// Gets the type and size line, e.g. <c>JPG · 2.1 MB</c>.
    /// </summary>
    public string Details => Item is { } item ? $"{item.FileFormat.ToUpperInvariant()} · {Format.FileSize(item.SizeBytes)}" : string.Empty;

    /// <summary>
    /// Gets the media type, which picks the placeholder icon.
    /// </summary>
    public MediaType MediaType => Item?.MediaType ?? MediaType.Image;

    /// <summary>
    /// Gets a value indicating whether making the thumbnail failed, so the placeholder shows its error badge.
    /// </summary>
    public bool IsThumbnailFailed => Item?.ThumbnailFailed ?? false;

    /// <summary>
    /// Gets a value indicating whether the item is a favorite, which shows the heart on the tile (E11-S01).
    /// </summary>
    public bool IsFavorite => Item?.IsFavorite ?? false;

    /// <summary>
    /// Gets the status badges.
    /// </summary>
    public IReadOnlyList<TileBadge> Badges => Item is { } item ? TileBadge.For(item) : [];

    /// <summary>
    /// Gets a video's duration, e.g. <c>0:42</c>, or <see langword="null"/> for other items and before it's known.
    /// </summary>
    public string? DurationText => Item is { MediaType: MediaType.Video, Duration: { } duration } ? Format.Duration(duration) : null;

    /// <summary>
    /// Gets a value indicating whether the item is an animated image, which shows the animated marker.
    /// </summary>
    public bool IsAnimated => Item?.MediaType is MediaType.AnimatedImage;

    /// <summary>
    /// Gets the list view's Type cell: the file format, e.g. <c>JPG</c> (E14-S02).
    /// </summary>
    public string TypeText => Item?.FileFormat.ToUpperInvariant() ?? string.Empty;

    /// <summary>
    /// Gets the list view's Date taken cell, as the camera recorded it, or an empty text before it's known.
    /// </summary>
    public string DateTakenText => Item?.DateTaken is { } taken ? taken.ToString("g", CultureInfo.CurrentCulture) : string.Empty;

    /// <summary>
    /// Gets the list view's Size cell, e.g. <c>2.1 MB</c>.
    /// </summary>
    public string SizeText => Item is { } item ? Format.FileSize(item.SizeBytes) : string.Empty;

    /// <summary>
    /// Gets the list view's Resolution cell, e.g. <c>1920 × 1080</c>, or an empty text before it's known.
    /// </summary>
    public string ResolutionText => Item is { Width: { } width, Height: { } height }
        ? $"{width} × {height}"
        : string.Empty;

    /// <summary>
    /// Gets the list view's Duration cell: a video's or animation's length, or an empty text.
    /// </summary>
    public string LengthText => Item is { MediaType: not MediaType.Image, Duration: { } duration } ? Format.Duration(duration) : string.Empty;

    /// <summary>
    /// Gets the list view's Status cell, e.g. <c>Indexed</c>.
    /// </summary>
    public string StatusText => Item switch
    {
        null => string.Empty,
        { IsFolderOffline: true } => Localizer.Current["Tile_Badge_Offline"],
        { } item => Localizer.Current[$"Details_Status_{item.Status}"],
    };

    /// <summary>
    /// Gets a value indicating whether the item's file can't be used now, because its folder is Offline or the file is
    /// Missing, so the tile shows dimmed with its badge (E14-S07, D025).
    /// </summary>
    public bool IsUnavailable => Item is { IsFolderOffline: true } or { Status: MediaItemStatus.Missing };

    /// <summary>
    /// Gets the list view's Folder cell: the folder the file is in.
    /// </summary>
    public string Folder => Item?.Folder ?? string.Empty;

    /// <summary>
    /// Gets a value indicating whether the item still has no thumbnail to show, so it's worth boosting (D150).
    /// </summary>
    public bool NeedsThumbnail => Item is { ThumbnailFailed: false } && Thumbnail is null;

    /// <summary>
    /// Points the tile at a result and starts loading its thumbnail. Showing the same item again keeps its thumbnail,
    /// unless the item's file or thumbnail state changed.
    /// </summary>
    /// <param name="index">The result position.</param>
    /// <param name="item">The item, or <see langword="null"/> while its page loads.</param>
    /// <param name="size">The thumbnail size the grid uses now.</param>
    /// <param name="match">Why the item is in the search results, or <see langword="null"/>.</param>
    public void Show(int index, MediaItemSummary? item, ThumbnailSize size, ResultMatch? match = null)
    {
        if (item is null && Item is not null && Index == index)
        {
            // The page is reloading; keep what's shown until it arrives.
            return;
        }

        Match = match;
        var keep = item is not null && Item is { } current && current.Id == item.Id && current.FileModifiedAt == item.FileModifiedAt
            && current.ThumbnailFailed == item.ThumbnailFailed && size == thumbnailSize && (Thumbnail is not null || loading is not null);
        Index = index;
        Item = item;
        if (keep)
        {
            return;
        }

        Clear();
        thumbnailSize = size;
        if (item is not null && Environment.GetEnvironmentVariable("MFC_NO_THUMBS") is null)
        {
            _ = LoadAsync(item, size);
        }
    }

    /// <summary>
    /// Stops loading and lets go of the thumbnail, e.g. when the tile scrolled away.
    /// </summary>
    public void Clear()
    {
        loading?.Cancel();
        loading?.Dispose();
        loading = null;
        Thumbnail = null;
        lease?.Dispose();
        lease = null;
    }

    private async Task LoadAsync(MediaItemSummary item, ThumbnailSize size)
    {
        var cancellation = new CancellationTokenSource();
        loading = cancellation;
        ThumbnailLease? loaded;
        try
        {
            loaded = await loader.LoadAsync(item, size, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cancellation.IsCancellationRequested || !ReferenceEquals(loading, cancellation) || Item?.Id != item.Id)
        {
            loaded?.Dispose();
            return;
        }

        loading = null;
        cancellation.Dispose();
        lease = loaded;
        Thumbnail = loaded?.Bitmap;
    }
}
