using System.ComponentModel;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Platform;
using MediaFileClassifier.UseCases.Browsing.ItemLocation;
using MediaFileClassifier.UseCases.Browsing.Selection;
using MediatR;

namespace MediaFileClassifier.Ui.Browser;

/// <summary>
/// The media browser's item commands for the context menu and the Item menu (E14-S04-T02): open in the default app,
/// reveal in File Explorer and copy file path, through the platform services. Open and Reveal act on the focused
/// item and are disabled, with the reason, while its file is missing or its folder offline (D025). Copy file path
/// copies the paths of all selected items, one per line.
/// </summary>
public sealed class ItemCommands : ICommandContributor
{
    /// <summary>Command ID: open the focused item in its default app.</summary>
    public const string OpenInDefaultApp = "item.open-default-app";

    /// <summary>Command ID: show the focused item's file in the file explorer.</summary>
    public const string Reveal = "item.reveal";

    /// <summary>Command ID: copy the selected items' file paths.</summary>
    public const string CopyPath = "item.copy-path";

    private readonly SelectionModel selection;
    private readonly ISender sender;
    private readonly IPlatformServices platform;
    private readonly ITextClipboard clipboard;
    private readonly Localizer localizer;
    private readonly List<AppCommand> commands = [];
    private Id<MediaItem>? loadedFor;
    private ItemLocation? focused;

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemCommands"/> class.
    /// </summary>
    /// <param name="selection">The media browser's selection.</param>
    /// <param name="sender">Sends the location queries.</param>
    /// <param name="platform">Opens and reveals files.</param>
    /// <param name="clipboard">Takes the copied paths.</param>
    /// <param name="localizer">UI strings, for the reasons a command is disabled.</param>
    public ItemCommands(SelectionModel selection, ISender sender, IPlatformServices platform, ITextClipboard clipboard, Localizer localizer)
    {
        this.selection = selection;
        this.sender = sender;
        this.platform = platform;
        this.clipboard = clipboard;
        this.localizer = localizer;
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
        var id = selection.FocusedId;
        if (id == loadedFor)
        {
            return;
        }

        loadedFor = id;
        focused = null;
        var location = id is { } item ? await LocateAsync(item) : null;
        if (loadedFor != id)
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

    private async Task OpenAsync()
    {
        if (selection.FocusedId is { } id && await LocateAsync(id) is { Availability: ItemAvailability.Available } location)
        {
            await RunAsync(() => platform.OpenWithDefaultAppAsync(location.Location));
        }
    }

    private async Task RevealAsync()
    {
        if (selection.FocusedId is { } id && await LocateAsync(id) is { Availability: ItemAvailability.Available } location)
        {
            await RunAsync(() => platform.RevealInFileExplorerAsync(location.Location));
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

    private static async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is IOException or Win32Exception or UnauthorizedAccessException)
        {
            // The file went away since the last scan; the next scan marks it Missing.
        }
    }
}
