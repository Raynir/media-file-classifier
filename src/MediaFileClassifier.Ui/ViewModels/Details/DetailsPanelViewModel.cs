using System.ComponentModel;
using Avalonia;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Details;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Shell;
using MediaFileClassifier.Ui.ViewModels.Browser;
using MediaFileClassifier.UseCases.Browsing.ItemDetails;
using MediaFileClassifier.UseCases.Browsing.ItemLocation;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PreviewSource = MediaFileClassifier.Ui.Details.PreviewKind;
using TechnicalRowBuilder = MediaFileClassifier.Ui.Details.TechnicalRows;

namespace MediaFileClassifier.Ui.ViewModels.Details;

/// <summary>
/// The preview and details panel (E14-S05): the focused item's preview, name, quick actions, and its Overview and
/// Technical tabs. It follows the media browser's focused item while something is selected, and fills the shell's
/// details region only then. Enter or a double-click in the grid shows the panel and moves keyboard focus to the
/// preview, where the arrow keys move through the results.
/// </summary>
public sealed partial class DetailsPanelViewModel : ViewModelBase, IItemOpener, IDisposable
{
    /// <summary>
    /// How long the focused item must stay before its original is decoded, so holding an arrow key doesn't decode
    /// every item it passes.
    /// </summary>
    internal static readonly TimeSpan OriginalDelay = TimeSpan.FromMilliseconds(150);

    private readonly SelectionModel selection;
    private readonly ISender sender;
    private readonly ShellRegions regions;
    private readonly ShellLayout layout;
    private readonly PreviewLoader previews;
    private readonly Localizer localizer;
    private readonly Func<ThumbnailGridViewModel> grid;
    private readonly ILogger logger;
    private CancellationTokenSource? loading;
    private PreviewImage? preview;
    private bool loadingFullResolution;
    private int loads;

    /// <summary>
    /// Initializes a new instance of the <see cref="DetailsPanelViewModel"/> class.
    /// </summary>
    /// <param name="selection">The media browser's selection, whose focused item the panel shows.</param>
    /// <param name="sender">Sends the details query.</param>
    /// <param name="regions">The shell regions; the panel fills the details region while it shows an item.</param>
    /// <param name="layout">The shell layout, which keeps the panel's preview height, collapse and tab.</param>
    /// <param name="previews">Loads the preview.</param>
    /// <param name="commands">The commands, for the quick actions.</param>
    /// <param name="localizer">UI strings.</param>
    /// <param name="grid">Gets the thumbnail grid, to give it back the keyboard focus.</param>
    /// <param name="logger">Logs previews that couldn't be loaded.</param>
    public DetailsPanelViewModel(
        SelectionModel selection,
        ISender sender,
        ShellRegions regions,
        ShellLayout layout,
        PreviewLoader previews,
        CommandRegistry commands,
        Localizer localizer,
        Func<ThumbnailGridViewModel> grid,
        ILogger<DetailsPanelViewModel>? logger = null)
    {
        this.selection = selection;
        this.sender = sender;
        this.regions = regions;
        this.layout = layout;
        this.previews = previews;
        this.localizer = localizer;
        this.grid = grid;
        this.logger = logger ?? NullLogger<DetailsPanelViewModel>.Instance;
        Commands = commands;
        selection.Changed += OnSelectionChanged;
        layout.PropertyChanged += OnLayoutChanged;
    }

    /// <summary>
    /// Raised when the preview should take keyboard focus, e.g. after Enter in the grid.
    /// </summary>
    public event EventHandler? FocusRequested;

    /// <summary>
    /// Gets the shown item, or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(Subtitle), nameof(Description), nameof(Tags), nameof(HasDescription), nameof(HasTags))]
    public partial ItemDetails? Item { get; private set; }

    /// <summary>
    /// Gets the panel's title: the item's title, or its file name.
    /// </summary>
    public string Title => Item?.Title ?? Item?.FileName ?? string.Empty;

    /// <summary>
    /// Gets the file name under the title, when the item has a title; otherwise the title is the file name.
    /// </summary>
    public string? Subtitle => Item?.Title is null ? null : Item.FileName;

    /// <summary>
    /// Gets a value indicating whether the preview should take keyboard focus once it's shown, e.g. after Enter in
    /// the grid while the panel was still empty.
    /// </summary>
    public bool IsFocusPending { get; private set; }

    /// <summary>
    /// Gets "N items selected" while several items are selected, or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    public partial string? SelectedCountText { get; private set; }

    /// <summary>
    /// Gets the Technical tab's rows.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<DetailRow> TechnicalRows { get; private set; } = [];

    /// <summary>
    /// Gets the item's description, or a placeholder.
    /// </summary>
    public string Description => Item?.Description ?? localizer.Get("Details_Description_None");

    /// <summary>
    /// Gets a value indicating whether the item has a description.
    /// </summary>
    public bool HasDescription => !string.IsNullOrWhiteSpace(Item?.Description);

    /// <summary>
    /// Gets the item's tags.
    /// </summary>
    public IReadOnlyList<string> Tags => Item?.Tags ?? [];

    /// <summary>
    /// Gets a value indicating whether the item has tags.
    /// </summary>
    public bool HasTags => Tags.Count > 0;

    /// <summary>
    /// Gets the preview bitmap, or <see langword="null"/> while there's none.
    /// </summary>
    [ObservableProperty]
    public partial Bitmap? PreviewBitmap { get; private set; }

    /// <summary>
    /// Gets how the preview bitmap must be turned to show it upright.
    /// </summary>
    [ObservableProperty]
    public partial ImageOrientation PreviewOrientation { get; private set; } = ImageOrientation.Normal;

    /// <summary>
    /// Gets the image's full size, orientation applied, or empty when unknown.
    /// </summary>
    [ObservableProperty]
    public partial Size PreviewImageSize { get; private set; }

    /// <summary>
    /// Gets the preview bitmap's size in pixels, as decoded.
    /// </summary>
    [ObservableProperty]
    public partial PixelSize PreviewBitmapSize { get; private set; }

    /// <summary>
    /// Gets where the preview bitmap comes from.
    /// </summary>
    [ObservableProperty]
    public partial PreviewSource? PreviewKind { get; private set; }

    /// <summary>
    /// Gets a note under the preview, e.g. that it shows the thumbnail because the folder is offline.
    /// </summary>
    [ObservableProperty]
    public partial string? PreviewNote { get; private set; }

    /// <summary>
    /// Gets or sets a value indicating whether the preview is shown (not collapsed).
    /// </summary>
    public bool IsPreviewExpanded
    {
        get => !layout.IsDetailsPreviewCollapsed;
        set => layout.IsDetailsPreviewCollapsed = !value;
    }

    /// <summary>
    /// Gets or sets the preview's height.
    /// </summary>
    public double PreviewHeight
    {
        get => layout.DetailsPreviewHeight;
        set => layout.DetailsPreviewHeight = Math.Clamp(value, ShellLayout.MinPreviewHeight, ShellLayout.MaxPreviewHeight);
    }

    /// <summary>
    /// Gets or sets the open tab's index: 0 for Overview, 1 for Technical.
    /// </summary>
    public int SelectedTab
    {
        get => (int)layout.DetailsTab;
        set => layout.DetailsTab = value == (int)DetailsTab.Technical ? DetailsTab.Technical : DetailsTab.Overview;
    }

    /// <summary>
    /// Gets the commands, for the quick actions.
    /// </summary>
    public CommandRegistry Commands { get; }

    /// <summary>
    /// Gets or sets the screen's longer side in pixels, the size the original is first decoded to.
    /// </summary>
    public int ScreenPixels { get; set; } = 2560;

    /// <summary>
    /// Gets a task for the latest load, for tests.
    /// </summary>
    internal Task Loading { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Shows an item in the panel (Enter or double-click in the grid): opens the panel if it's hidden or collapsed,
    /// and moves keyboard focus to the preview. The panel already follows the focused item.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <returns><see langword="true"/>.</returns>
    public Task<bool> OpenAsync(Id<MediaItem> itemId)
    {
        layout.Details.Expand();
        IsFocusPending = true;
        FocusRequested?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(true);
    }

    /// <summary>
    /// Records that the view gave the preview keyboard focus.
    /// </summary>
    public void OnFocused() => IsFocusPending = false;

    /// <summary>
    /// Gives the keyboard focus back to the grid, e.g. Esc in the panel.
    /// </summary>
    public void ReturnToBrowser() => grid().RequestFocus();

    /// <summary>
    /// Tells the panel the preview's zoom, so it decodes the original at full resolution once the screen-sized
    /// bitmap no longer has enough detail.
    /// </summary>
    /// <param name="zoom">Image pixels per screen pixel.</param>
    /// <returns>A task that completes when the sharper bitmap is shown.</returns>
    public Task OnZoomChangedAsync(double zoom)
    {
        if (preview is not { Kind: PreviewSource.ScreenSized } shown || loadingFullResolution || Item is not { } item)
        {
            return Task.CompletedTask;
        }

        // Compare the longer sides, which don't depend on the orientation.
        var needed = Math.Max(shown.SourceSize.Width, shown.SourceSize.Height) * zoom;
        var available = Math.Max(shown.Size.Width, shown.Size.Height);
        return needed > available ? LoadFullResolutionAsync(item) : Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        selection.Changed -= OnSelectionChanged;
        layout.PropertyChanged -= OnLayoutChanged;
        loading?.Cancel();
        loading?.Dispose();
        SetPreview(null);
    }

    [RelayCommand]
    private void TogglePreview() => IsPreviewExpanded = !IsPreviewExpanded;

    private void OnSelectionChanged(object? sender, EventArgs e) => Loading = LoadAsync();

    private void OnLayoutChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ShellLayout.IsDetailsPreviewCollapsed):
                OnPropertyChanged(nameof(IsPreviewExpanded));
                break;
            case nameof(ShellLayout.DetailsPreviewHeight):
                OnPropertyChanged(nameof(PreviewHeight));
                break;
            case nameof(ShellLayout.DetailsTab):
                OnPropertyChanged(nameof(SelectedTab));
                break;
        }
    }

    private async Task LoadAsync()
    {
        var load = ++loads;
        var count = selection.Count;
        SelectedCountText = count > 1 ? localizer.Format("Details_SelectedCount", Format.Number(count)) : null;
        var id = count > 0 ? selection.FocusedId : null;
        if (id is null)
        {
            Show(null);
            return;
        }

        loading?.Cancel();
        loading?.Dispose();
        loading = new CancellationTokenSource();
        var token = loading.Token;
        try
        {
            var item = await sender.Send(new GetItemDetailsQuery(id.Value), token);
            if (load != loads)
            {
                return;
            }

            var previous = Item;
            Show(item);
            if (item is null || (previous is not null && SamePreview(previous, item) && preview is not null))
            {
                return;
            }

            await LoadPreviewAsync(item, load, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // A newer selection took over.
        }
        catch (Exception ex)
        {
            LogLoadFailed(logger, ex);
        }
    }

    private async Task LoadPreviewAsync(ItemDetails item, int load, CancellationToken token)
    {
        SetPreview(null);
        var thumbnail = await previews.LoadThumbnailAsync(item, token);
        if (load != loads)
        {
            thumbnail?.Dispose();
            return;
        }

        SetPreview(thumbnail);
        PreviewNote = item.Location.Availability switch
        {
            ItemAvailability.Offline => localizer.Format("Details_Preview_Thumbnail_Offline", item.Location.FolderName),
            ItemAvailability.Missing => localizer.Get("Details_Preview_Thumbnail_Missing"),
            _ => thumbnail is null && item.MediaType == MediaType.Video ? localizer.Get("Details_Preview_None") : null,
        };
        if (!PreviewLoader.CanShowOriginal(item))
        {
            return;
        }

        await Task.Delay(OriginalDelay, token);
        PreviewImage original;
        try
        {
            original = await previews.LoadOriginalAsync(item, ScreenPixels, token);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LogOriginalFailed(logger, item.FileName, ex);
            if (load == loads)
            {
                PreviewNote = localizer.Get("Details_Preview_Thumbnail_Failed");
            }

            return;
        }

        if (load != loads)
        {
            original.Dispose();
            return;
        }

        SetPreview(original);
    }

    private async Task LoadFullResolutionAsync(ItemDetails item)
    {
        loadingFullResolution = true;
        var load = loads;
        try
        {
            var full = await previews.LoadOriginalAsync(item, maxLongestEdge: null, loading?.Token ?? default);
            if (load != loads)
            {
                full.Dispose();
                return;
            }

            SetPreview(full);
        }
        catch (OperationCanceledException)
        {
            // The item changed.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LogOriginalFailed(logger, item.FileName, ex);
        }
        finally
        {
            loadingFullResolution = false;
        }
    }

    private static bool SamePreview(ItemDetails shown, ItemDetails item) =>
        shown.Id == item.Id && shown.FileModifiedAt == item.FileModifiedAt && shown.Location.Availability == item.Location.Availability;

    private void Show(ItemDetails? item)
    {
        if (item is null)
        {
            loading?.Cancel();
            SetPreview(null);
            PreviewNote = null;
        }

        var sameItem = item is not null && Item?.Id == item.Id;
        Item = item;
        TechnicalRows = item is null ? [] : TechnicalRowBuilder.Build(item, localizer);
        if (TechnicalRowBuilder.ShownSize(item?.Technical) is { } size)
        {
            PreviewImageSize = new Size(size.Width, size.Height);
        }
        else if (!sameItem)
        {
            PreviewImageSize = default;
        }
        regions.Details = item is null ? null : this;
    }

    private void SetPreview(PreviewImage? image)
    {
        var old = preview;
        preview = image;
        PreviewBitmap = image?.Bitmap;
        PreviewOrientation = image?.Orientation ?? ImageOrientation.Normal;
        PreviewKind = image?.Kind;
        PreviewBitmapSize = image?.Size ?? default;
        if (image is { Kind: not PreviewSource.Thumbnail } && PreviewImageSize == default)
        {
            // Without extracted dimensions, the original's own size keeps zooming in true pixels.
            var source = image.SourceSize;
            PreviewImageSize = image.Orientation >= ImageOrientation.Transpose ? new Size(source.Height, source.Width) : new Size(source.Width, source.Height);
        }
        old?.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't load the details of the focused item")]
    private static partial void LogLoadFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't decode {FileName} for the preview; showing its thumbnail")]
    private static partial void LogOriginalFailed(ILogger logger, string fileName, Exception exception);
}
