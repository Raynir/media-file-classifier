using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Paths;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using MediaFileClassifier.Testing.UseCases;
using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Platform;
using MediaFileClassifier.Ui.Processing;
using MediaFileClassifier.Ui.Tests.Commands;
using MediaFileClassifier.UseCases.Browsing.ItemLocation;
using MediaFileClassifier.UseCases.Browsing.Selection;
using MediaFileClassifier.UseCases.Import.RetryFailedItems;

namespace MediaFileClassifier.Ui.Tests.Browser;

/// <summary>
/// Tests for the media browser's context menu and its item commands (E14-S04): only applicable entries, disabled
/// entries with a reason for missing and offline files (D025), and Open, Reveal and Copy file path.
/// </summary>
public sealed class ItemCommandsTests
{
    [Fact]
    public async Task Menu_WithNothingSelected_IsEmpty()
    {
        var bed = new Bed(10);

        await bed.Selection.ApplyAsync(2, SelectionGesture.FocusOnly);

        Assert.Empty(bed.Menu.Build());
    }

    [Fact]
    public async Task Menu_ForAnAvailableItem_OffersOpenRevealAndCopy_InBands()
    {
        var bed = new Bed(10);

        await bed.SelectAsync(2);

        var entries = bed.Menu.Build();
        Assert.Equal(
            [ItemCommands.OpenInDefaultApp, ItemCommands.Reveal, "-", ItemCommands.CopyPath],
            entries.Select(entry => (entry as AppCommand)?.Id ?? "-"));
        Assert.All(entries.OfType<AppCommand>(), command => Assert.True(command.IsEnabled));
    }

    [Theory]
    [InlineData(ItemAvailability.Missing, "The file is missing from its folder.")]
    [InlineData(ItemAvailability.Offline, "The folder Photos is offline. Connect the drive to use the file.")]
    public async Task Menu_ForAnUnavailableFile_DisablesOpenAndReveal_WithTheReason(ItemAvailability availability, string reason)
    {
        var bed = new Bed(10);
        bed.Availability[bed.Source.Ids[3]] = availability;

        await bed.SelectAsync(3);

        var commands = bed.Menu.Build().OfType<AppCommand>().ToDictionary(command => command.Id);
        Assert.Equal((false, reason), (commands[ItemCommands.OpenInDefaultApp].IsEnabled, commands[ItemCommands.OpenInDefaultApp].DisabledReason));
        Assert.Equal((false, reason), (commands[ItemCommands.Reveal].IsEnabled, commands[ItemCommands.Reveal].ToolTip));
        Assert.True(commands[ItemCommands.CopyPath].IsEnabled);
    }

    [Fact]
    public async Task Menu_WhereFilesCantBeRevealed_LeavesRevealOut()
    {
        var bed = new Bed(10, canReveal: false);

        await bed.SelectAsync(1);

        Assert.DoesNotContain(bed.Menu.Build().OfType<AppCommand>(), command => command.Id == ItemCommands.Reveal);
    }

    [Fact]
    public async Task OpenAndReveal_ActOnTheFocusedItem_AndSkipUnavailableFiles()
    {
        var bed = new Bed(10);
        await bed.SelectAsync(1);
        await bed.Selection.ApplyAsync(4, SelectionGesture.AddRange);

        await bed.Registry[ItemCommands.OpenInDefaultApp].ExecuteAsync();
        await bed.Registry[ItemCommands.Reveal].ExecuteAsync();
        bed.Availability[bed.Source.Ids[4]] = ItemAvailability.Missing;
        await bed.Registry[ItemCommands.OpenInDefaultApp].ExecuteAsync();

        Assert.Equal([$"IMG_{4}.jpg"], bed.Platform.Opened);
        Assert.Equal([$"IMG_{4}.jpg"], bed.Platform.Revealed);
    }

    [Fact]
    public async Task Shortcuts_WithTheFocusKeptButNothingSelected_DoNothing()
    {
        var bed = new Bed(10);
        await bed.SelectAsync(2);
        bed.Selection.Clear();
        await bed.Commands.Loading;

        await bed.Registry[ItemCommands.OpenInDefaultApp].ExecuteAsync();
        await bed.Registry[ItemCommands.Reveal].ExecuteAsync();
        await bed.Registry[ItemCommands.CopyPath].ExecuteAsync();

        Assert.Empty(bed.Platform.Opened);
        Assert.Empty(bed.Platform.Revealed);
        Assert.Null(bed.Clipboard.Text);
    }

    [Fact]
    public async Task Menu_AfterTheFocusedFileGoesMissing_ShowsTheNewStateOnTheNextRightClick()
    {
        var bed = new Bed(10);
        await bed.SelectAsync(3);
        bed.Availability[bed.Source.Ids[3]] = ItemAvailability.Missing;

        // A right-click on the selected tile only focuses it.
        await bed.Selection.ApplyAsync(3, SelectionGesture.FocusOnly);
        await bed.Commands.Loading;

        var open = bed.Menu.Build().OfType<AppCommand>().Single(command => command.Id == ItemCommands.OpenInDefaultApp);
        Assert.False(open.IsEnabled);
    }

    [Fact]
    public async Task Open_WhenThePlatformFails_DoesNotThrow()
    {
        var bed = new Bed(10);
        bed.Platform.Failure = new System.ComponentModel.Win32Exception("No app is associated with this file.");
        await bed.SelectAsync(1);

        await bed.Registry[ItemCommands.OpenInDefaultApp].ExecuteAsync();

        Assert.Empty(bed.Platform.Opened);
    }

    [Fact]
    public async Task Menu_LeavesOutCommandsNotForContextMenus()
    {
        var bed = new Bed(10, extra: new HiddenItemCommand());

        await bed.SelectAsync(1);

        Assert.DoesNotContain(bed.Menu.Build().OfType<AppCommand>(), command => command.Id == HiddenItemCommand.Id);
    }

    [Fact]
    public async Task CopyPath_CopiesEverySelectedPath_OnePerLine()
    {
        var bed = new Bed(10);
        await bed.SelectAsync(1);
        await bed.Selection.ApplyAsync(3, SelectionGesture.Range);

        await bed.Registry[ItemCommands.CopyPath].ExecuteAsync();

        var lines = bed.Clipboard.Text!.Split(Environment.NewLine);
        Assert.Equal(
            new[] { 1, 2, 3 }.Select(index => Path.Combine(@"D:\Photos", $"IMG_{index}.jpg")).Order(),
            lines.Order());
    }

    [Fact]
    public async Task RetrySelected_IsInTheMenu_AndRetriesTheSelectedItems()
    {
        var bed = new Bed(10, extraFor: bed => new RetryCommands(bed.Sender, bed.Selection, Localizer.Current));
        await bed.Selection.ApplyAsync(2, SelectionGesture.Replace);
        await bed.Selection.ApplyAsync(4, SelectionGesture.Toggle);

        var command = bed.Menu.Build().OfType<AppCommand>().Single(entry => entry.Id == RetryCommands.RetrySelected);
        await command.ExecuteAsync();

        var sent = Assert.Single(bed.Retries);
        Assert.Null(sent.JobId);
        Assert.Equal(new HashSet<Id<MediaItem>> { bed.Source.Ids[2], bed.Source.Ids[4] }, sent.ItemIds!.ToHashSet());
    }

    [Fact]
    public async Task RetryLibrary_RetriesEveryFailedItem_OnlyWithALibraryOpen()
    {
        var session = new FakeBrowserSession(open: false);
        var bed = new Bed(10, extraFor: bed => new RetryCommands(bed.Sender, bed.Selection, Localizer.Current, session));
        var command = bed.Registry[RetryCommands.RetryLibrary];
        Assert.False(command.IsEnabled);

        await session.SetCurrentAsync(new FakeBrowserSession().Current);
        command.Refresh();

        await command.ExecuteAsync();

        var sent = Assert.Single(bed.Retries);
        Assert.Equal((null, null), (sent.JobId, sent.ItemIds));
    }

    /// <summary>
    /// A selection over fake results, with the item commands and the context menu around it.
    /// </summary>
    private sealed class Bed
    {
        public Bed(int count, bool canReveal = true, ICommandContributor? extra = null, Func<Bed, ICommandContributor>? extraFor = null)
        {
            Source = new FakeResultSource(count);
            Selection = new SelectionModel(() => Source);
            Selection.KnownId = index => index >= 0 && index < Source.Ids.Count ? Source.Ids[index] : null;
            Selection.Reset(count);
            Platform = new RecordingPlatform(canReveal);
            Commands = new ItemCommands(Selection, new LocationSender(this), Platform, Clipboard, Localizer.Current);
            extra ??= extraFor?.Invoke(this);
            Registry = new CommandRegistry(extra is null ? [Commands] : [Commands, extra], Localizer.Current, new FakeCapabilityGate());
            Menu = new BrowserContextMenu(Registry, Localizer.Current);
        }

        public FakeResultSource Source { get; }

        public SelectionModel Selection { get; }

        public RecordingPlatform Platform { get; }

        public RecordingClipboard Clipboard { get; } = new();

        public ItemCommands Commands { get; }

        public CommandRegistry Registry { get; }

        public BrowserContextMenu Menu { get; }

        public Dictionary<Id<MediaItem>, ItemAvailability> Availability { get; } = [];

        public async Task SelectAsync(int index)
        {
            await Selection.ApplyAsync(index, SelectionGesture.Replace);
            await Commands.Loading;
        }

        public ItemLocation Locate(Id<MediaItem> id)
        {
            var index = Source.Ids.ToList().IndexOf(id);
            return new ItemLocation(
                id,
                new FileLocation(new FolderHandle(@"D:\Photos", "Photos"), $"IMG_{index}.jpg"),
                Path.Combine(@"D:\Photos", $"IMG_{index}.jpg"),
                "Photos",
                Availability.GetValueOrDefault(id));
        }

        public IReadOnlyList<Id<MediaItem>> Resolve(ItemSelection selection) => [.. Source.Ids.Where(selection.Contains)];

        public List<RetryFailedItemsCommand> Retries { get; } = [];

        public FakeSender Sender => new LocationSender(this);

        public RetryFailedItemsResult Retry(RetryFailedItemsCommand command)
        {
            Retries.Add(command);
            return new RetryFailedItemsResult(command.ItemIds?.Count ?? 0, 0);
        }
    }

    private sealed class LocationSender(Bed bed) : FakeSender
    {
        protected override object? Handle(object request) => request switch
        {
            GetItemLocationsQuery query => query.ItemIds.Select(bed.Locate).ToList(),
            ResolveItemSelectionQuery query => bed.Resolve(query.Selection),
            RetryFailedItemsCommand command => bed.Retry(command),
            _ => throw new NotSupportedException(),
        };
    }

    private sealed class RecordingPlatform(bool canReveal) : IPlatformServices
    {
        public List<string> Opened { get; } = [];

        public List<string> Revealed { get; } = [];

        public IAppPaths Paths => throw new NotSupportedException();

        public bool CanRevealInFileExplorer => canReveal;

        public Exception? Failure { get; set; }

        public Task<FolderHandle?> PickFolderAsync(string title, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<FileLocation>> PickFilesAsync(FilePickerRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task RevealInFileExplorerAsync(FileLocation location, CancellationToken cancellationToken = default)
        {
            Revealed.Add(location.RelativePath);
            return Task.CompletedTask;
        }

        public Task OpenWithDefaultAppAsync(FileLocation location, CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
            {
                return Task.FromException(Failure);
            }

            Opened.Add(location.RelativePath);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingClipboard : ITextClipboard
    {
        public string? Text { get; private set; }

        public Task SetTextAsync(string text)
        {
            Text = text;
            return Task.CompletedTask;
        }
    }

    private sealed class HiddenItemCommand : ICommandContributor
    {
        public const string Id = "item.hidden";

        public void Register(CommandRegistry registry) =>
            registry.Add(new AppCommand(Id, CommandGroups.Item, 10, "Command_OpenItem", () => { }) { InContextMenu = false });
    }
}
