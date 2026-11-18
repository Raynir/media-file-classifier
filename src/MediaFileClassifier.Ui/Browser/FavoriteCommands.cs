using Avalonia.Controls;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.UseCases.Browsing.Selection;
using MediaFileClassifier.UseCases.Favorites.GetFavoriteState;
using MediaFileClassifier.UseCases.Favorites.ToggleFavorite;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Ui.Browser;

/// <summary>
/// The Favorite command (E11-S01-T02) in the Item menu, the context menu, the preview panel and with <c>Ctrl+D</c>
/// (E16-S02). It acts on the whole selection: it unmarks the items when all are favorites, and marks them all
/// otherwise. Menus show it checked while all selected items are favorites.
/// </summary>
public sealed partial class FavoriteCommands : ICommandContributor
{
    /// <summary>Command ID: toggle the favorite mark of the selected items.</summary>
    public const string Favorite = "item.favorite";

    private readonly SelectionModel selection;
    private readonly ISender sender;
    private readonly ILogger logger;
    private AppCommand? command;
    private int loads;
    private bool allFavorite;

    /// <summary>
    /// Initializes a new instance of the <see cref="FavoriteCommands"/> class.
    /// </summary>
    /// <param name="selection">The media browser's selection.</param>
    /// <param name="sender">Sends the use cases.</param>
    /// <param name="logger">Logs a state that could not be read.</param>
    public FavoriteCommands(SelectionModel selection, ISender sender, ILogger<FavoriteCommands>? logger = null)
    {
        this.selection = selection;
        this.sender = sender;
        this.logger = logger ?? NullLogger<FavoriteCommands>.Instance;
    }

    /// <summary>
    /// Gets a task for the latest load of the selection's favorite state, for tests.
    /// </summary>
    internal Task Loading { get; private set; } = Task.CompletedTask;

    /// <inheritdoc />
    public void Register(CommandRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        command = registry.Add(new AppCommand(Favorite, CommandGroups.Item, 100, "Command_Favorite", ToggleAsync)
        {
            IconKey = "Icon.Favorite",
            ToggleType = MenuItemToggleType.CheckBox,
            IsCheckedRule = () => allFavorite,
            AppliesTo = () => selection.Count > 0,
        });
        selection.Changed += (_, _) => Reload();
    }

    /// <summary>
    /// Reads the favorite state of the selection again, e.g. after a favorite changed.
    /// </summary>
    public void Reload() => Loading = LoadAsync();

    private async Task LoadAsync()
    {
        // A newer load wins, so a slow read for an earlier selection cannot overwrite the current state.
        var load = ++loads;
        var state = false;
        try
        {
            if (selection.Count > 0)
            {
                var ids = await sender.Send(new ResolveItemSelectionQuery(selection.Snapshot()));
                state = await sender.Send(new GetFavoriteStateQuery(ids));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogStateFailed(logger, ex);
        }

        if (load == loads)
        {
            allFavorite = state;
            command?.Refresh();
        }
    }

    private async Task ToggleAsync()
    {
        // Menus and shortcuts run the command without a caller to catch, so a failure is logged, not thrown.
        try
        {
            var ids = await sender.Send(new ResolveItemSelectionQuery(selection.Snapshot()));
            if (ids.Count > 0)
            {
                await sender.Send(new ToggleFavoriteCommand(ids));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogToggleFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't change the favorite mark of the selection")]
    private static partial void LogToggleFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't read the favorite state of the selection")]
    private static partial void LogStateFailed(ILogger logger, Exception exception);
}
