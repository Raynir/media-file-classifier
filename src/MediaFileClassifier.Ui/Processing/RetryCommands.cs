using Avalonia.Threading;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.UseCases.Browsing.Selection;
using MediaFileClassifier.UseCases.Import.RetryFailedItems;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Ui.Processing;

/// <summary>
/// The manual retry commands (E05-S04-T04): "Retry failed items" in the Library menu, for every failed item of the
/// library, and "Retry" in the Item menu and the context menu, for the selected items that failed. Per job, the
/// processing panel (E05-S03) sends <see cref="RetryFailedItemsCommand"/> with the job's ID.
/// </summary>
/// <param name="sender">Sends the retry use case.</param>
/// <param name="selection">The media browser's selection.</param>
/// <param name="localizer">UI strings.</param>
/// <param name="session">Holds the open library, or <c>null</c> where there's none.</param>
/// <param name="logger">Logs retries that couldn't start.</param>
public sealed partial class RetryCommands(
    ISender sender,
    SelectionModel selection,
    Localizer localizer,
    ILibrarySession? session = null,
    ILogger<RetryCommands>? logger = null) : ICommandContributor
{
    /// <summary>Command ID: retry every failed item of the open library.</summary>
    public const string RetryLibrary = "library.retry-failed";

    /// <summary>Command ID: retry the selected items that failed.</summary>
    public const string RetrySelected = "item.retry-failed";

    private readonly ILogger log = logger ?? NullLogger<RetryCommands>.Instance;

    /// <inheritdoc />
    public void Register(CommandRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        var library = registry.Add(new AppCommand(RetryLibrary, CommandGroups.Library, 135, "Command_RetryFailedItems", () => RetryAsync(null))
        {
            WhyDisabled = NoLibrary,
        });
        registry.Add(new AppCommand(RetrySelected, CommandGroups.Item, 800, "Command_RetrySelected", RetrySelectedAsync)
        {
            AppliesTo = () => selection.Count > 0,
        });
        if (session is not null)
        {
            session.CurrentChanged += (_, _) => Dispatcher.UIThread.Post(library.Refresh);
        }
    }

    private string? NoLibrary() => session?.Current is null ? localizer.Get("Command_NoLibraryOpen") : null;

    private async Task RetrySelectedAsync()
    {
        var ids = await sender.Send(new ResolveItemSelectionQuery(selection.Snapshot()));
        if (ids.Count > 0)
        {
            await RetryAsync(ids);
        }
    }

    private async Task RetryAsync(IReadOnlyCollection<Id<MediaItem>>? itemIds)
    {
        try
        {
            var result = await sender.Send(new RetryFailedItemsCommand(ItemIds: itemIds));
            LogRetried(result.Retried, result.Unchanged);
        }
        catch (InvalidOperationException exception)
        {
            // The library closed meanwhile.
            LogRetryFailed(exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Manual retry: {Retried} items queued again, {Unchanged} damaged files unchanged")]
    private partial void LogRetried(int retried, int unchanged);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The manual retry couldn't start")]
    private partial void LogRetryFailed(Exception exception);
}
