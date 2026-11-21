using System.ComponentModel;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Platform;
using MediaFileClassifier.Ui.Rescan;
using MediaFileClassifier.UseCases.Browsing.ItemLocation;
using MediaFileClassifier.UseCases.Browsing.Selection;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Ui.Browser;

/// <summary>
/// The media browser's item commands for the context menu and the Item menu (E14-S04-T02): open in the default app,
/// reveal in File Explorer and copy file path, through the platform services. Open and Reveal act on the focused
/// item and are disabled, with the reason, while its file is missing or its folder offline (D025). Copy file path
/// copies the paths of all selected items, one per line. A Missing item also offers the Library menu's "Remove missing
/// items…" (E14-S07).
/// </summary>
public sealed partial class ItemCommands : ICommandContributor
{
    /// <summary>Command ID: open the focused item in its default app.</summary>
    public const string OpenInDefaultApp = "item.open-default-app";

    /// <summary>Command ID: show the focused item's file in the file explorer.</summary>
    public const string Reveal = "item.reveal";

    /// <summary>Command ID: copy the selected items' file paths.</summary>
    public const string CopyPath = "item.copy-path";

    /// <summary>
    /// Command ID: the Library menu's "Remove missing items…" (D240), offered on a Missing item (E14-S07).
    /// </summary>
    public const string RemoveMissing = "item.remove-missing";

    private readonly SelectionModel selection;
    private readonly ISender sender;
    private readonly IPlatformServices platform;
    private readonly ITextClipboard clipboard;
    private readonly Localizer localizer;
    private readonly ILogger logger;
    private readonly List<AppCommand> commands = [];
    private int loads;
    private ItemLocation? focused;

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemCommands"/> class.
    /// </summary>
    /// <param name="selection">The media browser's selection.</param>
    /// <param name="sender">Sends the location queries.</param>
    /// <param name="platform">Opens and reveals files.</param>
    /// <param name="clipboard">Takes the copied paths.</param>
    /// <param name="localizer">UI strings, for the reasons a command is disabled.</param>
    /// <param name="logger">Logs files that couldn't be opened or revealed.</param>
    public ItemCommands(
        SelectionModel selection,
        ISender sender,
        IPlatformServices platform,
        ITextClipboard clipboard,
        Localizer localizer,
        ILogger<ItemCommands>? logger = null)
    {
        this.selection = selection;
        this.sender = sender;
        this.platform = platform;
        this.clipboard = clipboard;
        this.localizer = localizer;
        this.logger = logger ?? NullLogger<ItemCommands>.Instance;
    }

    /// <summary>
    /// Gets a task for the latest load of the focused item's location, for tests.
    /// </summary>
    internal Task Loading { get; private set; } = Task.CompletedTask;

    /// <inheritdoc />
    public void Register(CommandRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        commands.Add(registry.Add(new AppCommand(OpenInDefaultApp, CommandGroups.Item, 20, "Command_OpenInDefaultApp", OpenAsync)
        {
            AppliesTo = HasFocusedSelection,
            WhyDisabled = WhyUnavailable,
        }));
        commands.Add(registry.Add(new AppCommand(Reveal, CommandGroups.Item, 30, "Command_RevealInExplorer", RevealAsync)
        {
            AppliesTo = () => platform.CanRevealInFileExplorer && HasFocusedSelection(),
            WhyDisabled = WhyUnavailable,
        }));
        commands.Add(registry.Add(new AppCommand(CopyPath, CommandGroups.Item, 900, "Command_CopyPath", CopyPathAsync)
        {
            AppliesTo = () => selection.Count > 0,
        }));

        // The library-wide cleanup, with its own confirmation and count; only where the Library menu has it.
        commands.Add(registry.Add(new AppCommand(
            RemoveMissing,
            CommandGroups.Item,
            960,
            "Command_RemoveMissing",
            () => registry.TryGet(RescanCommands.RemoveMissing, out var command) ? command.ExecuteAsync() : Task.CompletedTask)
        {
            IconKey = "Icon.Delete",
            AppliesTo = () => HasFocusedSelection() && focused?.Availability == ItemAvailability.Missing
                && registry.TryGet(RescanCommands.RemoveMissing, out var command) && command.IsVisible,
        }));
        selection.Changed += (_, _) => Loading = LoadFocusedAsync();
    }

    private bool HasFocusedSelection() => selection.Count > 0 && selection.FocusedId is not null;

    private string? WhyUnavailable() => focused?.Availability switch
    {
        ItemAvailability.Missing => localizer.Get("Item_Unavailable_Missing"),
        ItemAvailability.Offline => localizer.Format("Item_Unavailable_Offline", focused.FolderName),
        _ => null,
    };

    private async Task LoadFocusedAsync()
    {
        // Every selection change reloads, even for the same item, so a right-click shows its current state, e.g.
        // after its folder went offline. A newer load wins.
        var load = ++loads;
        var id = selection.FocusedId;
        if (focused?.ItemId != id)
        {
            focused = null;
        }

        ItemLocation? location = null;
        try
        {
            location = id is { } item ? await LocateAsync(item) : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogLocateFailed(logger, ex);
        }

        if (load != loads)
        {
            return;
        }

        focused = location;
        foreach (var command in commands)
        {
            command.Refresh();
        }
    }

    private async Task<ItemLocation?> LocateAsync(Id<MediaItem> id) =>
        (await sender.Send(new GetItemLocationsQuery([id]))).FirstOrDefault();

    private Task OpenAsync() => WithFocusedFileAsync(location => platform.OpenWithDefaultAppAsync(location));

    private Task RevealAsync() => WithFocusedFileAsync(location => platform.RevealInFileExplorerAsync(location));

    private async Task WithFocusedFileAsync(Func<FileLocation, Task> action)
    {
        if (selection.FocusedId is not { } id || await LocateAsync(id) is not { Availability: ItemAvailability.Available } location)
        {
            return;
        }

        try
        {
            await action(location.Location);
        }
        catch (Exception ex) when (ex is IOException or Win32Exception or UnauthorizedAccessException)
        {
            // The file went away since the last scan, or no app handles it; the next scan marks a gone file Missing.
            LogFileActionFailed(logger, location.FullPath ?? location.Location.RelativePath, ex);
        }
    }

    private async Task CopyPathAsync()
    {
        var ids = await sender.Send(new ResolveItemSelectionQuery(selection.Snapshot()));
        if (ids.Count == 0)
        {
            return;
        }

        var locations = await sender.Send(new GetItemLocationsQuery(ids));
        var paths = locations.Select(location => location.FullPath ?? $"{location.FolderName}/{location.Location.RelativePath}");
        await clipboard.SetTextAsync(string.Join(Environment.NewLine, paths));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't open or reveal {Path}")]
    private static partial void LogFileActionFailed(ILogger logger, string path, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't locate the focused item")]
    private static partial void LogLocateFailed(ILogger logger, Exception exception);
}
