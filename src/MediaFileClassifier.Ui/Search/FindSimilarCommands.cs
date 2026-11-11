using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.UseCases.Browsing.LibraryItems;
using MediaFileClassifier.UseCases.Browsing.Selection;
using MediaFileClassifier.UseCases.Search.SearchResults;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Ui.Search;

/// <summary>
/// "Find similar" (E09-S03) in the Item menu, the context menu and the preview panel, with <c>Ctrl+Shift+F</c>
/// (E16-S02): lists the items like the selected one, which the search bar shows as a chip.
/// </summary>
/// <param name="sender">Sends the queries.</param>
/// <param name="selection">The media browser's selection.</param>
/// <param name="services">Resolves the search results when the command runs; the search bar follows them.</param>
public sealed class FindSimilarCommands(ISender sender, SelectionModel selection, IServiceProvider services) : ICommandContributor
{
    /// <summary>Command ID: find items similar to the selected one.</summary>
    public const string FindSimilar = "item.find-similar";

    /// <inheritdoc />
    public void Register(CommandRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.Add(new AppCommand(FindSimilar, CommandGroups.Item, 300, "Command_FindSimilar", FindSimilarAsync)
        {
            AppliesTo = () => selection.Count == 1,
        });
    }

    private async Task FindSimilarAsync()
    {
        var ids = await sender.Send(new ResolveItemSelectionQuery(selection.Snapshot()));
        if (ids.Count != 1)
        {
            return;
        }

        var summaries = await sender.Send(new GetItemSummariesQuery([.. ids]));
        if (summaries.Count == 1)
        {
            services.GetRequiredService<SearchResultSource>().SetSimilarTo(new SimilarSource(summaries[0].Id, summaries[0].FileName, summaries[0].FileModifiedAt));
        }
    }
}
