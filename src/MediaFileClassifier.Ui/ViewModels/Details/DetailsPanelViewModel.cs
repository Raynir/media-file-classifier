using System.ComponentModel;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Details;
using MediaFileClassifier.Ui.Editing;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Shell;
using MediaFileClassifier.Ui.ViewModels.Browser;
using MediaFileClassifier.UseCases.Browsing.Selection;
using MediaFileClassifier.UseCases.Browsing.ItemDetails;
using MediaFileClassifier.UseCases.Browsing.ItemLocation;
using MediaFileClassifier.UseCases.Libraries.CheckFolderAvailability;
using MediaFileClassifier.UseCases.Metadata.UndoBulkEdit;
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
    private readonly FolderStatusChanges? folderStatus;
    private CancellationTokenSource? loading;
    private CancellationTokenSource? previewLoading;
    private bool disposed;
    private ItemDetails? previewFor;
    private PreviewImage? preview;
    private bool loadingFullResolution;
    private int loads;
    private int previewLoads;
    private readonly UnsavedChangesGuard? guard;
    private readonly IDisposable? guardRegistration;
    private readonly IDisposable? bulkGuardRegistration;
    private bool wasExpanded;
    private readonly TimeProvider clock;
    private bool restoringPanel;
    private readonly IEjectNotifications? ejects;
    private IEjectRegistration? ejectRegistration;
    private FolderHandle? ejectFolder;

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
    /// <param name="guard">Asks about staged changes before the panel closes (E07-S12-T01).</param>
    /// <param name="undoStore">Holds what the last bulk edit changed, for Undo (E07-S03-T03).</param>
    /// <param name="video">Plays videos in the preview (E14-S06), or <see langword="null"/> to show only their thumbnail.</param>
    /// <param name="clock">Times the frames of an animated image; the system clock by default.</param>
    /// <param name="strip">The keyframe strip under the preview (E14-S13), or <see langword="null"/> for none.</param>
    /// <param name="folderStatus">Tells when a folder went Offline or came back, so the shown item reloads
    /// (E14-S07-T04), or <see langword="null"/>.</param>
    /// <param name="ejects">Tells when the drive of a shown video or animation is being ejected, so the preview lets go
    /// of the file (MFC-138), or <see langword="null"/> on platforms without eject requests.</param>
    public DetailsPanelViewModel(
        SelectionModel selection,
        ISender sender,
        ShellRegions regions,
        ShellLayout layout,
        PreviewLoader previews,
        CommandRegistry commands,
        Localizer localizer,
        Func<ThumbnailGridViewModel> grid,
        ILogger<DetailsPanelViewModel>? logger = null,
        UnsavedChangesGuard? guard = null,
        BulkUndoStore? undoStore = null,
        VideoPlayerViewModel? video = null,
        TimeProvider? clock = null,
        KeyframeStripViewModel? strip = null,
        FolderStatusChanges? folderStatus = null,
        IEjectNotifications? ejects = null)
    {
        this.clock = clock ?? TimeProvider.System;
        this.ejects = ejects;
        layout.Details.PropertyChanged += OnDetailsVisibilityChanged;
        this.selection = selection;
        this.sender = sender;
        this.regions = regions;
        this.layout = layout;
        this.previews = previews;
        this.localizer = localizer;
        this.grid = grid;
        this.logger = logger ?? NullLogger<DetailsPanelViewModel>.Instance;
        Commands = commands;
        Video = video;
        if (video is not null)
        {
            video.PropertyChanged += OnVideoChanged;
        }
        Strip = strip;
        if (strip is not null)
        {
            strip.Chosen += OnKeyframeChosen;
            strip.OpenPlayer = OpenInPlayer;
        }

        Editor = new MetadataEditorViewModel(sender, localizer, logger);
        Editor.PropertyChanged += OnEditorChanged;
        BulkEditor = new BulkEditorViewModel(sender, localizer, logger, undoStore);
        BulkEditor.PropertyChanged += OnBulkEditorChanged;
        selection.Changed += OnSelectionChanged;
        this.folderStatus = folderStatus;
        if (folderStatus is not null)
        {
            folderStatus.Changed += OnFolderStatusChanged;
        }

        layout.PropertyChanged += OnLayoutChanged;
        if (guard is not null)
        {
            // Closing the panel is guarded here; the guard registration is dropped on dispose.
            this.guard = guard;
            guardRegistration = guard.Register(new MetadataEditorStagedChanges(Editor, () => Title));
            bulkGuardRegistration = guard.Register(new BulkEditorStagedChanges(BulkEditor, () => Title));
            wasExpanded = layout.Details.IsExpanded;
            layout.Details.PropertyChanged += OnPanelChanged;
        }
    }

    /// <summary>
    /// Raised when the preview should take keyboard focus, e.g. after Enter in the grid.
    /// </summary>
    public event EventHandler? FocusRequested;

    /// <summary>
    /// Raised when the metadata editor should take keyboard focus (the Edit metadata command).
    /// </summary>
    public event EventHandler? EditRequested;

    /// <summary>
    /// Gets the shown item, or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(Subtitle), nameof(Description), nameof(Tags), nameof(HasDescription), nameof(HasTags))]
    public partial ItemDetails? Item { get; private set; }

    /// <summary>
    /// Gets the video player in the preview (E14-S06), or <see langword="null"/> without in-app playback.
    /// </summary>
    public VideoPlayerViewModel? Video { get; }

    /// <summary>
    /// Gets the keyframe strip under the preview (E14-S13), or <see langword="null"/> without one.
    /// </summary>
    public KeyframeStripViewModel? Strip { get; }

    /// <summary>
    /// Gets the metadata editor of the shown item (E07-S02).
    /// </summary>
    public MetadataEditorViewModel Editor { get; }

    /// <summary>
    /// Gets the metadata editor in bulk mode, shown instead of <see cref="Editor"/> while several items are selected (E07-S03).
    /// </summary>
    public BulkEditorViewModel BulkEditor { get; }

    /// <summary>
    /// Gets a value indicating whether the panel is in bulk mode: several items are selected, so the Overview tab shows
    /// what they share and edits them all at once.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(Subtitle), nameof(IsSingleEdit))]
    public partial bool IsBulk { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the Overview tab shows the single-item editor.
    /// </summary>
    public bool IsSingleEdit => !IsBulk;

    /// <summary>
    /// Gets a value indicating whether the panel keeps showing an item that is no longer the focused one, because the
    /// item has unsaved changes and the selection moved without the user choosing (E07-S12-T01), e.g. a filter
    /// removed the item from the results. The panel follows the selection again once the changes are saved or dropped.
    /// </summary>
    [ObservableProperty]
    public partial bool IsPinned { get; private set; }

    /// <summary>
    /// Gets the panel's title: the item's title, or its file name.
    /// </summary>
    public string Title => IsBulk && SelectedCountText is { } count ? count : Item?.Title ?? Item?.FileName ?? string.Empty;

    /// <summary>
    /// Gets the file name under the title, when the item has a title; otherwise the title is the file name.
    /// </summary>
    public string? Subtitle => IsBulk || Item?.Title is null ? null : Item.FileName;

    /// <summary>
    /// Gets a value indicating whether the preview should take keyboard focus once it's shown, e.g. after Enter in
    /// the grid while the panel was still empty.
    /// </summary>
    public bool IsFocusPending { get; private set; }

    /// <summary>
    /// Gets "N items selected" while several items are selected, or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
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
    [NotifyPropertyChangedFor(nameof(IsZoomAvailable))]
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
    /// Gets the animated GIF or WebP playing in the preview (E14-S12), or <see langword="null"/> for any other item.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsZoomAvailable))]
    public partial AnimatedPreviewViewModel? Animation { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the shown animated image is too large to play, so the preview shows its first
    /// frame and offers Open in default app (D070, D026).
    /// </summary>
    [ObservableProperty]
    public partial bool IsAnimationTooLarge { get; private set; }

    partial void OnIsAnimationTooLargeChanged(bool value) => UpdateFallback();

    /// <summary>
    /// Gets a value indicating whether the preview can be zoomed: there is a bitmap, and no animation or video plays
    /// in it.
    /// </summary>
    public bool IsZoomAvailable => PreviewBitmap is not null && Animation is null && Video is not { IsActive: true };

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

        // Enter shows the preview, so a collapsed one opens; the arrow keys work while it has focus.
        IsPreviewExpanded = true;
        IsFocusPending = true;
        FocusRequested?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(true);
    }

    /// <summary>
    /// Loads the shown item again if its metadata changed, e.g. after an edit in another view (E07-S01-T03).
    /// </summary>
    /// <param name="itemIds">The items that changed.</param>
    public void Reload(IReadOnlyCollection<Id<MediaItem>> itemIds) => Reload(itemIds, new HashSet<DescriptiveField>(), tagsChanged: false);

    /// <summary>
    /// Loads the shown item again if its metadata changed. Staged changes in the editor are never discarded: the
    /// editor refreshes what has nothing staged and keeps the rest (E07-S12-T02).
    /// </summary>
    /// <param name="itemIds">The items that changed.</param>
    /// <param name="changedFields">The fields that changed.</param>
    /// <param name="tagsChanged">Whether the tags changed.</param>
    public void Reload(IReadOnlyCollection<Id<MediaItem>> itemIds, IReadOnlySet<DescriptiveField> changedFields, bool tagsChanged)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        ArgumentNullException.ThrowIfNull(changedFields);

        if (IsBulk && !BulkEditor.IsSaving && BulkEditor.ItemIds.Any(itemIds.Contains))
        {
            // What the selection shares changed. Staged changes stay; a clean editor shows the new aggregate.
            if (BulkEditor.IsDirty)
            {
                BulkEditor.Notice = localizer.Get("Bulk_Notice_Changed");
            }
            else
            {
                _ = BulkEditor.LoadAsync(BulkEditor.ItemIds);
            }
        }

        if (Item is { } item && itemIds.Contains(item.Id))
        {
            Loading = LoadAsync();

            // Whether the editor is dirty is decided when the metadata arrives, so edits typed while it loads are kept
            // too. A clean editor just shows the stored values.
            _ = Editor.MergeChangesAsync(changedFields, tagsChanged);
        }
    }

    /// <summary>
    /// Opens the panel on the Overview tab and asks the metadata editor to take keyboard focus.
    /// </summary>
    public void RequestEdit()
    {
        layout.Details.Expand();
        layout.DetailsTab = DetailsTab.Overview;
        EditRequested?.Invoke(this, EventArgs.Empty);
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
        // The container disposes the panel twice: as itself and as the IItemOpener it also serves (MFC-1006).
        if (disposed)
        {
            return;
        }

        disposed = true;
        selection.Changed -= OnSelectionChanged;
        if (folderStatus is not null)
        {
            folderStatus.Changed -= OnFolderStatusChanged;
        }

        BulkEditor.PropertyChanged -= OnBulkEditorChanged;
        BulkEditor.Dispose();
        layout.PropertyChanged -= OnLayoutChanged;
        layout.Details.PropertyChanged -= OnPanelChanged;
        layout.Details.PropertyChanged -= OnDetailsVisibilityChanged;
        guardRegistration?.Dispose();
        bulkGuardRegistration?.Dispose();
        loading?.Cancel();
        loading?.Dispose();
        CancelPreview();
        StopListeningForEject();
        SetAnimation(null);
        SetPreview(null);
        if (Video is not null)
        {
            Video.PropertyChanged -= OnVideoChanged;
            Video.Dispose();
        }

        if (Strip is not null)
        {
            Strip.Chosen -= OnKeyframeChosen;
            Strip.Dispose();
        }
    }

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Save and Cancel apply only while there is something to save.
        if (e.PropertyName is nameof(MetadataEditorViewModel.IsDirty) or nameof(MetadataEditorViewModel.IsSaving))
        {
            foreach (var id in (string[])[MetadataEditorCommands.Save, MetadataEditorCommands.Cancel])
            {
                if (Commands.TryGet(id, out var command))
                {
                    command.Refresh();
                }
            }
        }

        if (e.PropertyName == nameof(MetadataEditorViewModel.IsDirty) && IsPinned && !Editor.IsDirty)
        {
            // The held item is saved or dropped: show what is selected now.
            IsPinned = false;
            Loading = LoadAsync();
        }
    }

    // Closing or collapsing the panel with staged changes can't wait for the answer, so the panel opens again at once
    // and the user is asked; once they chose to go on, it closes as requested.
    private void OnPanelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (guard is null || restoringPanel || e.PropertyName != nameof(SidePanel.IsExpanded))
        {
            return;
        }

        var panel = layout.Details;
        if (panel.IsExpanded)
        {
            wasExpanded = true;
            return;
        }

        if (!wasExpanded || !Editor.IsDirty)
        {
            wasExpanded = false;
            return;
        }

        var (visible, collapsed) = (panel.IsVisible, panel.IsCollapsed);
        SetPanel(expanded: true, visible, collapsed);
        _ = CloseAfterConfirmAsync(visible, collapsed);
    }

    private async Task CloseAfterConfirmAsync(bool visible, bool collapsed)
    {
        if (guard is not null && await guard.ConfirmLeaveAsync())
        {
            wasExpanded = false;
            SetPanel(expanded: false, visible, collapsed);
        }
    }

    private void SetPanel(bool expanded, bool visible, bool collapsed)
    {
        restoringPanel = true;
        try
        {
            if (expanded)
            {
                layout.Details.Expand();
            }
            else
            {
                layout.Details.IsVisible = visible;
                layout.Details.IsCollapsed = collapsed;
                layout.Details.IsDrawerOpen = false;
            }
        }
        finally
        {
            restoringPanel = false;
        }
    }

    private void OnVideoChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VideoPlayerViewModel.IsActive))
        {
            OnPropertyChanged(nameof(IsZoomAvailable));
        }

        if (e.PropertyName is nameof(VideoPlayerViewModel.IsActive) or nameof(VideoPlayerViewModel.FailureText))
        {
            UpdateFallback();
        }
    }

    // D026: a video that can't play in the app, or an animation too large to, shows its frame and the strip, and the
    // strip offers Open in player. Anything that plays here jumps to a chosen keyframe instead. An offline or missing
    // file plays nowhere: the strip selects the moment and says why it can't open it (MFC-1007, DNEW-6).
    private void UpdateFallback()
    {
        if (Strip is null)
        {
            return;
        }

        var item = Item;
        Strip.IsFileAvailable = item is { Location.Availability: ItemAvailability.Available };
        Strip.UnavailableReason = item?.Location is { } location ? WhyMomentsUnavailable(location) : null;
        Strip.IsFallback = item?.MediaType switch
        {
            MediaType.Video or MediaType.AnimatedImage when !Strip.IsFileAvailable => true,
            MediaType.Video => Video is null || Video.FailureText is not null,
            MediaType.AnimatedImage => IsAnimationTooLarge,
            _ => false,
        };
    }

    private string? WhyMomentsUnavailable(ItemLocation location) => location.Availability switch
    {
        ItemAvailability.Missing => localizer.Get("Keyframes_Unavailable_Missing"),
        ItemAvailability.Offline => OfflineSource.Describe(location.FolderName, location.VolumeLabel, localizer) is { } source
            ? localizer.Format("Keyframes_Unavailable_Offline", location.FolderName, source)
            : localizer.Format("Keyframes_Unavailable_OfflineFolder", location.FolderName),
        _ => null,
    };

    private void OnKeyframeChosen(object? sender, TimeSpan time)
    {
        if (Animation is { } animation)
        {
            animation.JumpTo(time);
        }
        else if (Video is { IsActive: true, FailureText: null } video)
        {
            video.JumpTo(time);
        }
    }

    private void OpenInPlayer()
    {
        if (Commands.TryGet(ItemCommands.OpenInDefaultApp, out var appCommand) && appCommand is System.Windows.Input.ICommand command && command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    private void OnDetailsVisibilityChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SidePanel.IsExpanded) && !layout.Details.IsExpanded)
        {
            Animation?.Pause();
        }
    }

    private void SetAnimation(AnimatedPreviewViewModel? animation)
    {
        var old = Animation;
        Animation = animation;
        IsAnimationTooLarge = false;
        old?.Dispose();
    }

    [RelayCommand]
    private void TogglePreview() => IsPreviewExpanded = !IsPreviewExpanded;

    private void OnSelectionChanged(object? sender, EventArgs e) => Loading = LoadAsync();

    // A folder went Offline or came back (E14-S07-T04): the shown item's preview, strip and Status row follow it.
    private void OnFolderStatusChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (!disposed && Item is { } item)
        {
            Reload([item.Id]);
        }
    });

    private void OnLayoutChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ShellLayout.IsDetailsPreviewCollapsed):
                OnPropertyChanged(nameof(IsPreviewExpanded));
                if (!IsPreviewExpanded)
                {
                    // A hidden animation must not keep running.
                    Animation?.Pause();
                }

                break;
            case nameof(ShellLayout.DetailsPreviewHeight):
                OnPropertyChanged(nameof(PreviewHeight));
                break;
            case nameof(ShellLayout.DetailsTab):
                OnPropertyChanged(nameof(SelectedTab));
                break;
        }
    }

    private async Task<IReadOnlyList<Id<MediaItem>>> ResolveSelectionAsync()
    {
        try
        {
            return await sender.Send(new ResolveItemSelectionQuery(selection.Snapshot()));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogLoadFailed(logger, ex);
            return [];
        }
    }

    private void OnBulkEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BulkEditorViewModel.IsDirty) or nameof(BulkEditorViewModel.IsSaving) or nameof(BulkEditorViewModel.CanUndo))
        {
            foreach (var id in (string[])[MetadataEditorCommands.Save, MetadataEditorCommands.Cancel, MetadataEditorCommands.UndoBulk, MetadataEditorCommands.UndoBulkShortcut])
            {
                if (Commands.TryGet(id, out var command))
                {
                    command.Refresh();
                }
            }
        }

        if (e.PropertyName == nameof(BulkEditorViewModel.IsDirty) && IsPinned && IsBulk && !BulkEditor.IsDirty)
        {
            // The held bulk changes are saved or dropped: show what is selected now.
            IsPinned = false;
            Loading = LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        var load = ++loads;
        var count = selection.Count;
        SelectedCountText = count > 1 ? localizer.Format("Details_SelectedCount", Format.Number(count)) : null;
        var id = count > 0 ? selection.FocusedId : null;
        if (Item is { } held && Editor.IsDirty && (id != held.Id || count > 1))
        {
            // The selection moved on its own (a prompt came before any gesture of the user): keep the item.
            IsPinned = true;
            Editor.Notice = localizer.Get("Editor_Notice_Held");
            return;
        }

        IReadOnlyList<Id<MediaItem>> selectedIds = [];
        if (count > 1)
        {
            // Bulk mode: the items the selection stands for, which a select-all keeps in the database until now.
            selectedIds = await ResolveSelectionAsync();
            if (load != loads)
            {
                return;
            }

            if (IsBulk && BulkEditor.IsDirty)
            {
                if (!BulkEditor.ItemIds.ToHashSet().SetEquals(selectedIds))
                {
                    // Another selection, with no prompt before it: keep the staged changes and their items.
                    IsPinned = true;
                    BulkEditor.Notice = localizer.Get("Bulk_Notice_Held");
                }

                return;
            }
        }
        else if (IsBulk && BulkEditor.IsDirty)
        {
            IsPinned = true;
            BulkEditor.Notice = localizer.Get("Bulk_Notice_Held");
            return;
        }

        IsPinned = false;
        IsBulk = count > 1;
        if (IsBulk)
        {
            _ = BulkEditor.LoadAsync(selectedIds);
        }
        else if (BulkEditor.ItemCount > 0)
        {
            _ = BulkEditor.LoadAsync([]);
        }

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

            Show(item);

            // The same file again, e.g. the selection refreshed during an import: keep its preview, even one still loading.
            if (item is not null && (previewFor is null || !SamePreview(previewFor, item)))
            {
                await LoadPreviewAsync(item);
            }
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

    private async Task LoadPreviewAsync(ItemDetails item)
    {
        CancelPreview();
        ListenForEject(item);
        Video?.Show(item);
        Strip?.Show(item);
        UpdateFallback();
        previewFor = item;
        var load = ++previewLoads;
        previewLoading = new CancellationTokenSource();
        var token = previewLoading.Token;
        try
        {
            SetAnimation(null);
            SetPreview(null);
            var canShowOriginal = PreviewLoader.CanShowOriginal(item);
            if (!canShowOriginal)
            {
                // Only the thumbnail will show, so zoom in its own pixels rather than the original's.
                PreviewImageSize = default;
            }

            var thumbnail = await previews.LoadThumbnailAsync(item, token);
            if (load != previewLoads)
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
            if (!canShowOriginal)
            {
                return;
            }

            await Task.Delay(OriginalDelay, token);
            if (item.MediaType == MediaType.AnimatedImage && await TryPlayAnimationAsync(item, load, token))
            {
                return;
            }

            var original = await previews.LoadOriginalAsync(item, ScreenPixels, token);
            if (load != previewLoads)
            {
                original.Dispose();
                return;
            }

            SetPreview(original);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Another item's preview took over.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LogOriginalFailed(logger, item.FileName, ex);
            if (load == previewLoads)
            {
                PreviewNote = localizer.Get("Details_Preview_Thumbnail_Failed");
            }
        }
    }

    private async Task<bool> TryPlayAnimationAsync(ItemDetails item, int load, CancellationToken token)
    {
        AnimatedImageLoad? loaded;
        try
        {
            loaded = await previews.LoadAnimationAsync(item, token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not IOException and not UnauthorizedAccessException)
        {
            // A file the animation decoder can't read: the usual original decides what shows.
            LogOriginalFailed(logger, item.FileName, ex);
            return false;
        }

        if (load != previewLoads)
        {
            (loaded?.Animation as IDisposable)?.Dispose();
            return true;
        }

        if (loaded?.Animation is not { } animation)
        {
            // One frame, or too large to play: the usual original shows, the first frame of the latter.
            if (loaded is { IsTooLarge: true })
            {
                IsAnimationTooLarge = true;
                PreviewNote = localizer.Format("Details_Preview_Animation_TooLarge", Format.Number(loaded.DecodedBytes / (1024 * 1024)), Format.Number(PreviewLoader.MaxAnimationBytes / (1024 * 1024)));
            }

            return false;
        }

        var player = new AnimatedPreviewViewModel(animation, clock, localizer);
        if (!IsPreviewExpanded || !layout.Details.IsExpanded)
        {
            // Nothing shows it, so it must not run.
            player.Pause();
        }

        SetAnimation(player);
        return true;
    }

    private async Task LoadFullResolutionAsync(ItemDetails item)
    {
        loadingFullResolution = true;
        var load = previewLoads;
        try
        {
            var full = await previews.LoadOriginalAsync(item, maxLongestEdge: null, previewLoading?.Token ?? default);
            if (load != previewLoads)
            {
                full.Dispose();
                return;
            }

            SetPreview(full);
        }
        catch (OperationCanceledException)
        {
            // Another item's preview took over.
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

    private void CancelPreview()
    {
        previewLoading?.Cancel();
        previewLoading?.Dispose();
        previewLoading = null;
    }

    // A video or a playing animation keeps its file open, which makes Windows refuse to eject its drive (MFC-138), so
    // the panel listens for eject requests of the folder while it shows one. Still images are read whole and closed.
    private void ListenForEject(ItemDetails item)
    {
        var folder = ejects is not null
            && item is { MediaType: MediaType.Video or MediaType.AnimatedImage, Location.Availability: ItemAvailability.Available }
            ? item.Location.Location.Folder
            : null;
        if (folder is not null && ejectRegistration is not null && folder == ejectFolder)
        {
            return;
        }

        StopListeningForEject();
        if (folder is null || ejects!.Register(folder) is not { } registration)
        {
            return;
        }

        registration.EjectRequested += OnEjectRequested;
        registration.EjectCancelled += OnEjectCancelled;
        ejectRegistration = registration;
        ejectFolder = folder;
    }

    private void StopListeningForEject()
    {
        if (ejectRegistration is { } registration)
        {
            registration.EjectRequested -= OnEjectRequested;
            registration.EjectCancelled -= OnEjectCancelled;
            registration.Dispose();
        }

        ejectRegistration = null;
        ejectFolder = null;
    }

    // Raised on the UI thread by the window's message hook. The file must be closed before it returns: Windows checks
    // for open handles right after every app has answered.
    private void OnEjectRequested(object? sender, EventArgs e)
    {
        if (disposed || previewFor is not { } item)
        {
            return;
        }

        CancelPreview();
        ++previewLoads;
        SetAnimation(null);
        Video?.Release();
        UpdateFallback();
        PreviewNote = localizer.Format("Details_Preview_Thumbnail_Ejecting", item.Location.FolderName);
    }

    // The eject was refused, e.g. another app holds the drive: the preview shows the file again.
    private void OnEjectCancelled(object? sender, EventArgs e)
    {
        if (disposed || previewFor is not { } item)
        {
            return;
        }

        PreviewNote = null;
        Loading = LoadPreviewAsync(item);
    }

    private static bool SamePreview(ItemDetails shown, ItemDetails item) =>
        shown.Id == item.Id && shown.FileModifiedAt == item.FileModifiedAt && shown.Location.Availability == item.Location.Availability;

    private void Show(ItemDetails? item)
    {
        if (item is null)
        {
            loading?.Cancel();
            CancelPreview();
            StopListeningForEject();
            ++previewLoads;
            previewFor = null;
            SetAnimation(null);
            SetPreview(null);
            PreviewNote = null;
            Video?.Show(null);
            Strip?.Show(null);
        }

        var sameItem = item is not null && Item?.Id == item.Id;
        Item = item;

        // The same item shown again, e.g. after a change elsewhere, keeps what the user has staged in the editor.
        if (!IsBulk && !(sameItem && Editor.IsDirty))
        {
            _ = Editor.LoadAsync(item?.Id);
        }

        TechnicalRows = item is null ? [] : TechnicalRowBuilder.Build(item, localizer);
        if (!sameItem)
        {
            // The same item keeps the size its preview set, e.g. a thumbnail's own for a video.
            PreviewImageSize = TechnicalRowBuilder.ShownSize(item?.Technical) is { } size ? new Size(size.Width, size.Height) : default;
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
