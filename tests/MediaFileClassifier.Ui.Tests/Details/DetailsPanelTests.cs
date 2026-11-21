using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Labs.AnimatedImage;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Previews;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.Infrastructure.Media.Thumbnails;
using MediaFileClassifier.Testing.Files;
using MediaFileClassifier.Testing.Media;
using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Details;
using MediaFileClassifier.Ui.Editing;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Shell;
using MediaFileClassifier.Ui.Tests.Browser;
using MediaFileClassifier.Ui.Tests.Commands;
using MediaFileClassifier.Ui.ViewModels.Details;
using MediaFileClassifier.Ui.Views.Details;
using MediaFileClassifier.UseCases.Browsing.ItemDetails;
using MediaFileClassifier.UseCases.Browsing.ItemKeyframes;
using MediaFileClassifier.UseCases.Browsing.Selection;
using MediaFileClassifier.UseCases.Browsing.ItemLocation;
using MediaFileClassifier.UseCases.Metadata.EditMetadata;
using MediaFileClassifier.UseCases.Metadata.GetBulkMetadata;
using MediaFileClassifier.UseCases.Metadata.GetItemMetadata;
using MediaFileClassifier.UseCases.Metadata.UndoBulkEdit;
using MediatR;
using SkiaSharp;

namespace MediaFileClassifier.Ui.Tests.Details;

/// <summary>
/// Tests for the preview and details panel (E14-S05): it follows the focused item, shows the selection count, loads
/// a screen-sized preview then full resolution when zoomed, falls back to the thumbnail (D025), and keeps its
/// layout in the shell layout.
/// </summary>
public sealed class DetailsPanelTests : IDisposable
{
    private readonly TemporaryFolder temp = new();

    public void Dispose()
    {
        temp.Dispose();
    }

    [Fact]
    public Task Panel_DisposedTwice_DoesNotThrow() => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 3);
        bed.Select(1, SelectionGesture.Replace);

        // The container disposes it as itself and as IItemOpener; the second time used to cancel a disposed token (MFC-1006).
        bed.Panel.Dispose();
        bed.Panel.Dispose();
    });

    [Fact]
    public Task Panel_FollowsTheFocusedItem_ShowsTheCount_AndLeavesTheRegionWhenNothingIsSelected() => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 10);

        bed.Select(2, SelectionGesture.Replace);
        Assert.Equal(bed.Source.Ids[2], bed.Panel.Item?.Id);
        Assert.Same(bed.Panel, bed.Regions.Details);
        Assert.Null(bed.Panel.SelectedCountText);
        Assert.Equal(("IMG_2.png", null), (bed.Panel.Title, bed.Panel.Subtitle));

        bed.Select(4, SelectionGesture.Range);
        Assert.Equal(bed.Source.Ids[4], bed.Panel.Item?.Id);
        Assert.Equal("3 items selected", bed.Panel.SelectedCountText);

        bed.Selection.Clear();
        bed.Settle();
        Assert.Null(bed.Panel.Item);
        Assert.Null(bed.Regions.Details);
    });

    [Fact]
    public Task AnimatedItem_Plays_AndStopsWhenTheSelectionChanges_OrTheSelectionIsCleared() => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 3);
        bed.AnimatedFile = CopySample("gif-variable-delays.gif");

        bed.Select(0, SelectionGesture.Replace);
        var animation = bed.Panel.Animation;
        Assert.NotNull(animation);
        Assert.True(animation.IsPlaying);
        Assert.Equal(13, animation.FrameCount);
        Assert.False(bed.Panel.IsAnimationTooLarge);
        Assert.False(bed.Panel.IsZoomAvailable);

        // Another item: the animation stops and lets its frames go.
        bed.AnimatedFile = null;
        bed.Select(1, SelectionGesture.Replace);
        Assert.Null(bed.Panel.Animation);
        Assert.False(animation.IsPlaying);
        Assert.Null(animation.PlaybackSource);
        Assert.True(bed.Panel.IsZoomAvailable);

        bed.AnimatedFile = CopySample("gif-variable-delays.gif");
        bed.Select(2, SelectionGesture.Replace);
        Assert.NotNull(bed.Panel.Animation);
        bed.Selection.Clear();
        bed.Settle();
        Assert.Null(bed.Panel.Animation);
    });

    [Fact]
    public Task AnimatedItem_Pauses_WhenThePreviewOrThePanelCollapses() => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 2);
        bed.AnimatedFile = CopySample("gif-variable-delays.gif");
        bed.Select(0, SelectionGesture.Replace);
        var animation = bed.Panel.Animation!;
        Assert.True(animation.IsPlaying);

        bed.Panel.IsPreviewExpanded = false;
        Assert.False(animation.IsPlaying);

        bed.Panel.IsPreviewExpanded = true;
        animation.Play();
        Assert.True(animation.IsPlaying);
        bed.Layout.Details.Collapse();
        Assert.False(animation.IsPlaying);
    });

    [Fact]
    public Task AnimatedItem_InTheView_ShowsThePlayerAndControls_AndSpaceCommaPeriodPlayAndStep() => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 2);
        bed.AnimatedFile = CopySample("gif-variable-delays.gif");
        bed.Select(0, SelectionGesture.Replace);
        var view = new DetailsPanelView { DataContext = bed.Panel };
        var window = new Window { Width = 400, Height = 800, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var animation = bed.Panel.Animation!;

        var player = view.FindControl<AnimatedImage>("AnimationPlayer")!;
        Assert.True(player.IsEffectivelyVisible);
        Assert.True(player.IsPlaying);
        Assert.False(view.FindControl<TextBlock>("ZoomText")!.IsEffectivelyVisible);
        Assert.True(view.FindControl<Button>("PlayPauseButton")!.IsEffectivelyVisible);

        // Keys on the preview: Space pauses, . steps forward, , steps back, Space plays again.
        var preview = view.GetVisualDescendants().OfType<ZoomImage>().Single();
        void Press(Key key) => preview.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
        Press(Key.Space);
        Assert.False(animation.IsPlaying);
        Assert.False(player.IsEffectivelyVisible);

        // Paused wherever the system clock got to: on a loaded machine that may be past the first frame.
        var paused = animation.FrameIndex;
        Press(Key.OemPeriod);
        Press(Key.OemPeriod);
        Assert.Equal((paused + 2) % animation.FrameCount, animation.FrameIndex);
        Press(Key.OemComma);
        Assert.Equal((paused + 1) % animation.FrameCount, animation.FrameIndex);
        Press(Key.Space);
        Assert.True(animation.IsPlaying);

        // The control swapped its source twice by now: the frames it played from must still be ours to show.
        Press(Key.Space);
        for (var i = 0; i < animation.FrameCount; i++)
        {
            Press(Key.OemPeriod);
            Assert.True(animation.CurrentFrame!.PixelSize.Width > 0);
        }

        // Buttons do the same.
        view.FindControl<Button>("StepForwardButton")!.Command!.Execute(null);
        Assert.False(animation.IsPlaying);
        window.Close();
    });

    [Fact]
    public Task AnimatedItem_SelectedWhileThePreviewIsCollapsed_DoesNotPlay() => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 1);
        bed.Panel.IsPreviewExpanded = false;
        bed.AnimatedFile = CopySample("gif-variable-delays.gif");

        bed.Select(0, SelectionGesture.Replace);

        Assert.NotNull(bed.Panel.Animation);
        Assert.False(bed.Panel.Animation.IsPlaying);
    });

    [Fact]
    public Task AnimatedItem_OneFrameOnly_ShowsTheUsualOriginal() => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 1);
        bed.AnimatedFile = CopySample("gif-static.gif");

        bed.Select(0, SelectionGesture.Replace);

        Assert.Null(bed.Panel.Animation);
        Assert.NotEqual(PreviewKind.Thumbnail, bed.Panel.PreviewKind);
    });

    [Fact]
    public Task AnimatedItem_TooLarge_ShowsTheFirstFrameWithANote() => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 1);
        var path = temp.Combine("huge.gif");
        File.WriteAllBytes(path, AnimatedPreviewTests.HugeGif(16000, 16000));
        bed.AnimatedFile = path;

        bed.Select(0, SelectionGesture.Replace);

        Assert.Null(bed.Panel.Animation);
        Assert.True(bed.Panel.IsAnimationTooLarge);
        Assert.Contains("over the 256 MB limit", bed.Panel.PreviewNote);
    });

    [Fact]
    public Task Preview_DecodesTheOriginalAtScreenSize_ThenAtFullResolutionWhenZoomedIn() => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 3);
        bed.Panel.ScreenPixels = 100;

        bed.Select(1, SelectionGesture.Replace);

        Assert.Equal(PreviewKind.ScreenSized, bed.Panel.PreviewKind);
        Assert.Equal(new PixelSize(100, 75), bed.Panel.PreviewBitmapSize);
        Assert.Equal(ImageOrientation.Rotate90, bed.Panel.PreviewOrientation);
        Assert.Equal(new Size(300, 400), bed.Panel.PreviewImageSize);

        // Fitted in a small preview, the screen-sized bitmap is enough; at 100% it isn't.
        bed.Wait(bed.Panel.OnZoomChangedAsync(0.2));
        Assert.Equal(PreviewKind.ScreenSized, bed.Panel.PreviewKind);
        bed.Wait(bed.Panel.OnZoomChangedAsync(1));
        Assert.Equal(PreviewKind.FullResolution, bed.Panel.PreviewKind);
        Assert.Equal(new PixelSize(400, 300), bed.Panel.PreviewBitmapSize);
    });

    [Fact]
    public Task Preview_WithoutExtractedDimensions_TakesTheOriginalsSize() => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 3) { WithoutTechnical = true };
        bed.Panel.ScreenPixels = 100;

        bed.Select(1, SelectionGesture.Replace);
        Assert.Equal(new Size(400, 300), bed.Panel.PreviewImageSize);
        bed.Wait(bed.Panel.OnZoomChangedAsync(1));
        Assert.Equal(PreviewKind.FullResolution, bed.Panel.PreviewKind);

        bed.Select(1, SelectionGesture.FocusOnly);
        Assert.Equal(new Size(400, 300), bed.Panel.PreviewImageSize);
    });

    [Theory]
    [InlineData(ItemAvailability.Offline, "Showing the thumbnail: the folder Photos is offline.")]
    [InlineData(ItemAvailability.Missing, "Showing the thumbnail: the file is missing.")]
    public Task Preview_ForAnUnavailableFile_ShowsTheThumbnail(ItemAvailability availability, string note) => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 3);
        bed.Availability = availability;

        bed.Select(0, SelectionGesture.Replace);

        Assert.Equal(PreviewKind.Thumbnail, bed.Panel.PreviewKind);
        Assert.Equal(ImageOrientation.Normal, bed.Panel.PreviewOrientation);
        Assert.Equal(note, bed.Panel.PreviewNote);
        Assert.Equal(0, bed.Decoder.Decodes);

        // Only the thumbnail shows, so 1:1 is the thumbnail's own pixels, also after a refresh.
        Assert.Equal(default, bed.Panel.PreviewImageSize);
        bed.Select(0, SelectionGesture.FocusOnly);
        Assert.Equal(default, bed.Panel.PreviewImageSize);
    });

    [Theory]
    [InlineData(ItemAvailability.Offline, "Backup-2TB", "Showing the thumbnail: the folder Photos is offline.", "The folder Photos is offline. Connect drive 'Backup-2TB' to open these moments in a player.")]
    [InlineData(ItemAvailability.Offline, null, "Showing the thumbnail: the folder Photos is offline.", "The folder Photos is offline, so these moments can't be opened in a player until it's back.")]
    [InlineData(ItemAvailability.Missing, null, "Showing the thumbnail: the file is missing.", "The file is missing, so these moments can't be opened in a player.")]
    public Task UnavailableVideo_SaysWhyInThePreviewAndTheStrip_AndItsMomentsStaySelectable(ItemAvailability availability, string? label, string note, string reason) => TestApp.RunAsync(() =>
    {
        // MFC-1007: before, the strip listed the keyframes with no Open in player and no reason, and a chosen one did nothing.
        var bed = new Bed(this, 3, withStrip: true) { Availability = availability, Type = MediaType.Video, VolumeLabel = label };

        bed.Select(0, SelectionGesture.Replace);
        Pump.Until(() => bed.Strip!.HasKeyframes);

        var strip = bed.Strip!;
        Assert.Equal(note, bed.Panel.PreviewNote);
        Assert.True(strip.IsFallback);
        Assert.False(strip.CanOpenPlayer);
        Assert.Equal(reason, strip.UnavailableNote);

        strip.Choose(strip.Keyframes[0]);
        Assert.Equal("Selected moment: 02:37", strip.MomentText);
    });

    [Theory]
    [InlineData(MediaType.Video)]
    [InlineData(MediaType.AnimatedImage)]
    public Task AvailableFile_HasNoUnavailableNote(MediaType type) => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 3, withStrip: true) { Type = type };

        bed.Select(0, SelectionGesture.Replace);
        Pump.Until(() => bed.Strip!.HasKeyframes);

        Assert.Null(bed.Strip!.UnavailableNote);
        Assert.Equal(type == MediaType.Video, bed.Strip.CanOpenPlayer);
    });

    [Fact]
    public Task Preview_OfAnUndecodableFile_KeepsTheThumbnail() => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 3);
        bed.Corrupt = true;

        bed.Select(0, SelectionGesture.Replace);

        Assert.Equal(PreviewKind.Thumbnail, bed.Panel.PreviewKind);
        Assert.Equal("Showing the thumbnail: the file couldn't be read.", bed.Panel.PreviewNote);
    });

    [Fact]
    public Task Reselecting_TheSameItem_KeepsItsPreview() => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 3);
        bed.Select(1, SelectionGesture.Replace);
        var shown = bed.Panel.PreviewBitmap;

        bed.Select(1, SelectionGesture.FocusOnly);

        Assert.Same(shown, bed.Panel.PreviewBitmap);
        Assert.Equal(1, bed.Decoder.Decodes);
    });

    [Fact]
    public Task SelectionRefresh_WhileTheOriginalDecodes_DoesNotStopIt() => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 3);

        // An import refreshes the selection while the item's original is still loading.
        var first = bed.Selection.ApplyAsync(1, SelectionGesture.Replace);
        bed.Wait(first);
        bed.Wait(bed.Selection.ApplyAsync(1, SelectionGesture.FocusOnly));
        Pump.Until(() => bed.Panel.PreviewKind == PreviewKind.FullResolution);

        Assert.Equal(1, bed.Decoder.Decodes);
    });

    [Fact]
    public Task Open_ShowsAHiddenOrCollapsedPanelAndPreview_AndAsksForFocus() => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 3);
        var focusRequests = 0;
        bed.Panel.FocusRequested += (_, _) => focusRequests++;
        bed.Layout.Details.IsVisible = false;

        bed.Wait(bed.Panel.OpenAsync(bed.Source.Ids[0]));
        Assert.True(bed.Layout.Details.IsExpanded);

        bed.Layout.Details.Collapse();
        bed.Layout.IsDetailsPreviewCollapsed = true;
        bed.Wait(bed.Panel.OpenAsync(bed.Source.Ids[0]));
        Assert.True(bed.Layout.Details.IsExpanded);
        Assert.False(bed.Layout.IsDetailsPreviewCollapsed);
        Assert.Equal(2, focusRequests);
    });

    [Fact]
    public Task Reload_AfterAnEditInAnotherView_ShowsTheNewValue_OnlyForTheShownItem() => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 3);
        bed.Select(1, SelectionGesture.Replace);
        bed.ItemTitle = "Rex";

        bed.Panel.Reload([bed.Source.Ids[2]]);
        bed.Settle();
        Assert.Null(bed.Panel.Item?.Title);

        bed.Panel.Reload([bed.Source.Ids[0], bed.Source.Ids[1]]);
        bed.Settle();
        Assert.Equal(("Rex", "IMG_1.png"), (bed.Panel.Title, bed.Panel.Subtitle));
    });

    [Fact]
    public Task EditMetadataCommand_NeedsASelection_ShowsTheOverviewTab_AndAsksTheEditorForFocus() => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 2);
        bed.Select(0, SelectionGesture.Replace);
        var requests = 0;
        bed.Panel.EditRequested += (_, _) => requests++;
        bed.Layout.Details.Collapse();
        bed.Layout.DetailsTab = DetailsTab.Technical;
        var edit = bed.Registry[MetadataEditorCommands.Edit];

        Assert.True(edit.IsApplicable);
        bed.Wait(edit.ExecuteAsync());

        Assert.Equal(1, requests);
        Assert.Equal(DetailsTab.Overview, bed.Layout.DetailsTab);
        Assert.False(bed.Layout.Details.IsCollapsed);

        bed.Select(0, SelectionGesture.Toggle);
        Assert.False(edit.IsApplicable);
    });

    [Fact]
    public Task SaveAndCancelCommands_ApplyOnlyWhileTheEditorHasUnsavedChanges() => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 1);
        bed.Select(0, SelectionGesture.Replace);
        bed.Wait(bed.Panel.Editor.Loading);
        var save = bed.Registry[MetadataEditorCommands.Save];
        var cancel = bed.Registry[MetadataEditorCommands.Cancel];
        Assert.False(save.IsApplicable);

        bed.Panel.Editor.AddTag("new");

        Assert.True(save.IsApplicable);
        Assert.True(cancel.IsApplicable);

        bed.Wait(cancel.ExecuteAsync());

        Assert.False(save.IsApplicable);
        Assert.False(cancel.IsApplicable);
    });

    [Fact]
    public Task SaveAndCancelCommands_InBulkMode_FollowTheBulkEditor_AndSaveAsksFirstForManyItems() => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 30) { BulkView = BulkMetadataView.Empty with { ItemCount = 30 } };
        bed.Select(0, SelectionGesture.Replace);
        bed.Select(29, SelectionGesture.Range);
        bed.Wait(bed.Panel.BulkEditor.Loading);
        var save = bed.Registry[MetadataEditorCommands.Save];
        var cancel = bed.Registry[MetadataEditorCommands.Cancel];
        var undo = bed.Registry[MetadataEditorCommands.UndoBulk];
        Assert.False(save.IsApplicable);
        Assert.False(undo.IsApplicable);

        bed.Panel.BulkEditor.AddTag("trip");

        Assert.True(save.IsApplicable);
        Assert.True(cancel.IsApplicable);

        bed.Wait(save.ExecuteAsync());

        Assert.True(bed.Panel.BulkEditor.IsConfirming);

        bed.Wait(cancel.ExecuteAsync());

        Assert.False(bed.Panel.BulkEditor.IsDirty);
        Assert.False(save.IsApplicable);
        Assert.False(bed.Panel.Editor.IsDirty);
    });

    [Fact]
    public Task UndoShortcut_WorksOnlyInBulkMode_ButEditUndoWorksWheneverThereIsSomethingToUndo() => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 30) { BulkView = BulkMetadataView.Empty with { ItemCount = 30 } };
        var menu = bed.Registry[MetadataEditorCommands.UndoBulk];
        var shortcut = bed.Registry[MetadataEditorCommands.UndoBulkShortcut];
        bed.Select(0, SelectionGesture.Replace);
        bed.Wait(bed.Panel.Editor.Loading);
        Assert.False(menu.IsApplicable);

        bed.Undo.Set(new Dictionary<Id<MediaItem>, UserLayerSnapshot> { [bed.Source.Ids[0]] = new([], [], [], []) });
        Dispatcher.UIThread.RunJobs();

        Assert.True(menu.IsApplicable);
        Assert.False(bed.Panel.IsBulk);
        Assert.False(shortcut.IsApplicable);

        bed.Select(5, SelectionGesture.Range);
        bed.Wait(bed.Panel.BulkEditor.Loading);

        Assert.True(bed.Panel.IsBulk);
        Assert.True(menu.IsApplicable);
        Assert.True(shortcut.IsApplicable);
    });

    [Fact]
    public Task Sections_AreKeptInTheShellLayout() => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 3);
        var changed = new List<string?>();
        bed.Panel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        bed.Panel.SelectedTab = 1;
        bed.Panel.TogglePreviewCommand.Execute(null);
        bed.Panel.PreviewHeight = 5_000;

        Assert.Equal((DetailsTab.Technical, true, ShellLayout.MaxPreviewHeight), (bed.Layout.DetailsTab, bed.Layout.IsDetailsPreviewCollapsed, bed.Layout.DetailsPreviewHeight));
        Assert.Equal([nameof(DetailsPanelViewModel.SelectedTab), nameof(DetailsPanelViewModel.IsPreviewExpanded), nameof(DetailsPanelViewModel.PreviewHeight)], changed);
    });

    [Fact]
    public Task View_TakesTheFocus_ShowsTheTabs_AndTheZoomButtonsSwitchBetweenFitAnd100Percent() => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 3);
        bed.Panel.ScreenPixels = 100;
        bed.Select(1, SelectionGesture.Replace);

        // Enter in the grid before the view exists: the preview takes the focus once it's shown.
        bed.Wait(bed.Panel.OpenAsync(bed.Source.Ids[1]));
        var view = new DetailsPanelView { DataContext = bed.Panel };
        var window = new Window { Width = 320, Height = 800, Content = view };
        window.Show();
        bed.Settle();
        Dispatcher.UIThread.RunJobs();

        var preview = view.GetVisualDescendants().OfType<ZoomImage>().Single();
        Assert.True(preview.IsFocused);
        Assert.False(bed.Panel.IsFocusPending);
        Assert.True(preview.Zoom < 1);
        view.FindControl<Button>("ActualSizeButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, preview.Zoom, 6);
        view.FindControl<Button>("FitButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(preview.Zoom < 1);

        // The next item, as large as this one, starts fitted.
        view.FindControl<Button>("ActualSizeButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        bed.Select(2, SelectionGesture.Replace);
        Assert.True(preview.Zoom < 1);

        var tabs = view.GetVisualDescendants().OfType<TabControl>().Single();
        Assert.Equal(2, tabs.ItemCount);
        tabs.SelectedIndex = 1;
        Assert.Equal(DetailsTab.Technical, bed.Layout.DetailsTab);
        window.Close();
    });

    [Theory]
    [InlineData(UnsavedChangesChoice.Stay, 2, true, 0)]
    [InlineData(UnsavedChangesChoice.Discard, 5, false, 0)]
    [InlineData(UnsavedChangesChoice.Save, 5, false, 1)]
    public Task SelectingAnotherItem_WithStagedChanges_AsksFirst_AndFollowsTheAnswer(UnsavedChangesChoice choice, int shownIndex, bool stillDirty, int saves) => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 10);
        bed.Select(2, SelectionGesture.Replace);
        bed.Panel.Editor.Descriptive[0].Text = "Edited";
        var prompts = new List<string>();
        bed.Guard.Ask = prompt =>
        {
            prompts.Add(prompt.Message);
            return Task.FromResult(choice);
        };

        bed.Select(5, SelectionGesture.Replace);

        Assert.Equal(["You have unsaved changes to \"IMG_2.png\". Save them before you go on?"], prompts);
        Assert.Equal(bed.Source.Ids[shownIndex], bed.Panel.Item?.Id);
        Assert.Equal(stillDirty, bed.Panel.Editor.IsDirty);
        Assert.Equal(saves, bed.Saved.Count);
    });

    [Fact]
    public Task SelectingAnotherItem_WithoutStagedChanges_OrClickingTheSameItem_AsksNothing() => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 10);
        var asked = 0;
        bed.Guard.Ask = _ =>
        {
            asked++;
            return Task.FromResult(UnsavedChangesChoice.Stay);
        };
        bed.Select(2, SelectionGesture.Replace);
        bed.Select(3, SelectionGesture.Replace);
        bed.Panel.Editor.Descriptive[0].Text = "Edited";

        bed.Select(3, SelectionGesture.Replace);

        Assert.Equal(0, asked);
        Assert.Equal(bed.Source.Ids[3], bed.Panel.Item?.Id);
    });

    [Fact]
    public Task SelectionMovingOnItsOwn_WithStagedChanges_KeepsTheItem_UntilTheChangesAreDropped() => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 10);
        bed.Select(2, SelectionGesture.Replace);
        bed.Panel.Editor.Descriptive[0].Text = "Edited";

        bed.Selection.Clear();
        bed.Settle();

        Assert.Equal(bed.Source.Ids[2], bed.Panel.Item?.Id);
        Assert.True(bed.Panel.IsPinned);
        Assert.Equal("Edited", bed.Panel.Editor.Descriptive[0].Text);
        Assert.NotNull(bed.Panel.Editor.Notice);

        bed.Panel.Editor.Cancel();
        bed.Settle();

        Assert.False(bed.Panel.IsPinned);
        Assert.Null(bed.Panel.Item);
        Assert.Null(bed.Panel.Editor.Notice);
    });

    [Theory]
    [InlineData(UnsavedChangesChoice.Stay, true)]
    [InlineData(UnsavedChangesChoice.Discard, false)]
    public Task CollapsingThePanel_WithStagedChanges_AsksFirst_AndOnlyClosesWhenTheUserGoesOn(UnsavedChangesChoice choice, bool stillExpanded) => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 3);
        bed.Select(1, SelectionGesture.Replace);
        bed.Panel.Editor.Descriptive[0].Text = "Edited";
        var asked = 0;
        bed.Guard.Ask = _ =>
        {
            asked++;
            return Task.FromResult(choice);
        };

        bed.Layout.Details.Collapse();
        Pump.Until(() => asked > 0 && bed.Layout.Details.IsExpanded == stillExpanded);

        Assert.Equal(1, asked);
        Assert.Equal(stillExpanded, bed.Layout.Details.IsExpanded);
        Assert.Equal(!stillExpanded, bed.Layout.Details.IsCollapsed);
    });

    [Fact]
    public Task CollapsingThePanel_WithoutStagedChanges_AsksNothing() => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 3);
        bed.Select(1, SelectionGesture.Replace);
        bed.Guard.Ask = _ => throw new InvalidOperationException("No prompt expected.");

        bed.Layout.Details.Collapse();

        Assert.True(bed.Layout.Details.IsCollapsed);
    });

    [Fact]
    public Task Reload_WithStagedChanges_KeepsThem_AndNamesAnOverlappingFieldInTheNotice() => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 3);
        bed.Select(1, SelectionGesture.Replace);
        bed.Panel.Editor.Descriptive[0].Text = "Edited";

        bed.Panel.Reload([bed.Source.Ids[1]], new HashSet<DescriptiveField> { DescriptiveField.Title }, tagsChanged: false);
        bed.Settle();
        Pump.Until(() => bed.Panel.Editor.Notice is not null);

        Assert.Equal("Edited", bed.Panel.Editor.Descriptive[0].Text);
        Assert.True(bed.Panel.Editor.IsDirty);
        Assert.Contains("changed elsewhere", bed.Panel.Editor.Notice);
    });

    private string CopySample(string name)
    {
        var path = temp.Combine(name);
        File.Copy(Path.Combine(SampleMedia.Directory, name), path, overwrite: true);
        return path;
    }

    private string WriteImage(string name, int width, int height)
    {
        var path = temp.Combine(name);
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.Teal);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    [Fact]
    public Task SelectingSeveralItems_SwitchesToBulkMode_AndOneItemSwitchesBack() => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 10);
        bed.Select(2, SelectionGesture.Replace);
        Assert.False(bed.Panel.IsBulk);

        bed.Select(4, SelectionGesture.Toggle);
        bed.Select(5, SelectionGesture.Toggle);

        Assert.True(bed.Panel.IsBulk);
        Assert.Equal("3 items selected", bed.Panel.Title);
        Assert.Null(bed.Panel.Subtitle);
        Assert.Equal(3, bed.Panel.BulkEditor.ItemIds.Count);
        Assert.Equal([bed.Source.Ids[2], bed.Source.Ids[4], bed.Source.Ids[5]], bed.BulkQueries[^1].ItemIds.OrderBy(id => bed.Source.Ids.ToList().IndexOf(id)));

        bed.Select(2, SelectionGesture.Replace);

        Assert.False(bed.Panel.IsBulk);
        Assert.Equal(0, bed.Panel.BulkEditor.ItemCount);
    });

    [Theory]
    [InlineData(UnsavedChangesChoice.Stay, true, 3)]
    [InlineData(UnsavedChangesChoice.Discard, false, 1)]
    public Task ChangingTheSelection_WithStagedBulkChanges_AsksFirst_AndFollowsTheAnswer(UnsavedChangesChoice choice, bool stillBulk, int selectedAfter) => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 10);
        bed.Select(2, SelectionGesture.Replace);
        bed.Select(3, SelectionGesture.Toggle);
        bed.Select(4, SelectionGesture.Toggle);
        bed.Panel.BulkEditor.AddTag("trip");
        var prompts = new List<string>();
        bed.Guard.Ask = prompt =>
        {
            prompts.Add(prompt.Message);
            return Task.FromResult(choice);
        };

        bed.Select(7, SelectionGesture.Replace);

        Assert.Equal(["You have unsaved changes to \"3 items selected\". Save them before you go on?"], prompts);
        Assert.Equal(stillBulk, bed.Panel.IsBulk);
        Assert.Equal(selectedAfter, bed.Selection.Count);
        Assert.Equal(stillBulk, bed.Panel.BulkEditor.IsDirty);
    });

    [Fact]
    public Task TheSelectionMovingOnItsOwn_WithStagedBulkChanges_KeepsThemAndTheirItems() => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 10);
        bed.Select(2, SelectionGesture.Replace);
        bed.Select(3, SelectionGesture.Toggle);
        bed.Panel.BulkEditor.AddTag("trip");
        var items = bed.Panel.BulkEditor.ItemIds;
        bed.Guard.Ask = _ => throw new InvalidOperationException("No prompt expected.");

        // A change that no gesture of the user caused, e.g. a filter removing the items from the results.
        bed.Selection.Reset(10);
        bed.Settle();

        Assert.True(bed.Panel.IsPinned);
        Assert.True(bed.Panel.IsBulk);
        Assert.Same(items, bed.Panel.BulkEditor.ItemIds);
        Assert.True(bed.Panel.BulkEditor.IsDirty);
        Assert.NotNull(bed.Panel.BulkEditor.Notice);
    });

        /// <summary>
    /// A selection over fake results, with the panel and fakes for the details, files and thumbnails.
    /// </summary>
    private sealed class Bed : ISender
    {
        private readonly DetailsPanelTests test;
        private readonly string original;
        private readonly string corrupt;

        public Bed(DetailsPanelTests test, int count, bool withStrip = false)
        {
            this.test = test;
            original = test.WriteImage("original.png", 400, 300);
            corrupt = test.temp.Combine("corrupt.png");
            File.WriteAllBytes(corrupt, [1, 2, 3, 4]);
            Source = new FakeResultSource(count);
            Selection = new SelectionModel(() => Source);
            Selection.KnownId = index => index >= 0 && index < Source.Ids.Count ? Source.Ids[index] : null;
            Selection.Reset(count);
            var thumbnails = new FakeThumbnailCache();
            foreach (var id in Source.Ids)
            {
                thumbnails.Files[(id, ThumbnailSize.Large)] = test.WriteImage($"thumb-{id}.png", 64, 48);
            }

            var loader = new ThumbnailLoader(new FakeBrowserSession(), thumbnails, new ThumbnailBitmapCache());
            Registry = new CommandRegistry([new MetadataEditorCommands(Selection, () => Panel!)], Localizer.Current, new FakeCapabilityGate());
            Strip = withStrip ? new KeyframeStripViewModel(this, null, null, Localizer.Current) : null;
            Panel = new DetailsPanelViewModel(
                Selection, this, Regions, Layout, new PreviewLoader(loader, new FakeFiles(), Decoder), Registry, Localizer.Current, () => throw new NotSupportedException(), guard: Guard, undoStore: Undo, strip: Strip);
            Selection.ConfirmChange = Guard.ConfirmLeaveAsync;
        }

        public UnsavedChangesGuard Guard { get; } = new(Localizer.Current);

        public List<EditMetadataCommand> Saved { get; } = [];

        public List<GetBulkMetadataQuery> BulkQueries { get; } = [];

        public BulkMetadataView BulkView { get; set; } = BulkMetadataView.Empty;

        public CommandRegistry Registry { get; }

        public BulkUndoStore Undo { get; } = new();

        public FakeResultSource Source { get; }

        public SelectionModel Selection { get; }

        public ShellRegions Regions { get; } = new();

        public ShellLayout Layout { get; } = new();

        public CountingDecoder Decoder { get; } = new();

        public DetailsPanelViewModel Panel { get; }

        public ItemAvailability Availability { get; set; }

        public KeyframeStripViewModel? Strip { get; }

        public MediaType? Type { get; set; }

        public string? VolumeLabel { get; set; }

        public bool Corrupt { get; set; }

        public string? AnimatedFile { get; set; }

        public bool WithoutTechnical { get; set; }

        public string? ItemTitle { get; set; }

        public void Select(int index, SelectionGesture gesture)
        {
            Wait(Selection.ApplyAsync(index, gesture));
            Settle();
        }

        public void Settle() => Wait(Panel.Loading);

        public void Wait(Task task) => Pump.Until(() => task.IsCompleted);

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is GetItemMetadataQuery)
            {
                return Task.FromResult((TResponse)(object)null!);
            }

            if (request is ResolveItemSelectionQuery)
            {
                return Task.FromResult((TResponse)(object)(IReadOnlyList<Id<MediaItem>>)[.. Source.Ids.Where(Selection.IsSelected)]);
            }

            if (request is GetBulkMetadataQuery bulk)
            {
                BulkQueries.Add(bulk);
                return Task.FromResult((TResponse)(object)BulkView);
            }

            if (request is GetItemKeyframesQuery)
            {
                return Task.FromResult((TResponse)(object)(IReadOnlyList<KeyframeMarker>)[new KeyframeMarker(Id<Keyframe>.New(), 0, TimeSpan.FromSeconds(157))]);
            }

            if (request is EditMetadataCommand edit)
            {
                Saved.Add(edit);
                return Task.FromResult((TResponse)(object)new EditMetadataResult([.. edit.ItemIds], null));
            }

            var query = Assert.IsType<GetItemDetailsQuery>(request);
            var index = Source.Ids.ToList().IndexOf(query.ItemId);
            var path = AnimatedFile ?? (Corrupt ? corrupt : original);
            var location = new ItemLocation(
                query.ItemId, new FileLocation(new FolderHandle(test.temp.Path, "Photos"), Path.GetFileName(path)), path, "Photos", Availability, VolumeLabel);
            var technical = new TechnicalMetadata
            {
                ItemId = query.ItemId,
                Width = 400,
                Height = 300,
                Orientation = ImageOrientation.Rotate90,
                ExtractedAt = DateTimeOffset.UnixEpoch,
            };
            var details = new ItemDetails(
                query.ItemId, $"IMG_{index}.png", "png", Type ?? (AnimatedFile is null ? MediaType.Image : MediaType.AnimatedImage), 1000, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, MediaItemStatus.Indexed, null, location, WithoutTechnical ? null : technical, ItemTitle, null, []);
            return Task.FromResult((TResponse)(object)details);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>
    /// Decodes with Skia, counting the decodes of screen-sized previews.
    /// </summary>
    private sealed class CountingDecoder : IPreviewDecoder
    {
        private readonly SkiaPreviewDecoder inner = new();

        public int Decodes { get; private set; }

        public Task<DecodedImage> DecodeAsync(MediaContent content, int? maxLongestEdge, CancellationToken cancellationToken)
        {
            if (maxLongestEdge is not null)
            {
                Decodes++;
            }

            return inner.DecodeAsync(content, maxLongestEdge, cancellationToken);
        }
    }

    /// <summary>
    /// Files are read from their full paths, so the file source isn't used.
    /// </summary>
    private sealed class FakeFiles : IMediaFileSource
    {
        public Task<Stream> OpenReadAsync(FileLocation file, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(File.OpenRead(Path.Combine(file.Folder.Id, file.RelativePath)));

        public IAsyncEnumerable<MediaFileStat> EnumerateFilesAsync(FolderHandle folder, string relativeDirectory = "", CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<FolderScanEntry> ScanAsync(FolderHandle folder, FolderScanOptions options, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<MediaFileStat?> GetStatAsync(FileLocation file, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> IsAvailableAsync(FolderHandle folder, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public string? GetPathInside(FolderHandle folder, FolderHandle parent) => throw new NotSupportedException();
    }
}
