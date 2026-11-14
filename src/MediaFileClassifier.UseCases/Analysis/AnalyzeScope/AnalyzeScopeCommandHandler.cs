using MediaFileClassifier.UseCases.Analysis.AnalyzeItems;
using MediatR;

namespace MediaFileClassifier.UseCases.Analysis.AnalyzeScope;

/// <summary>
/// Handles <see cref="AnalyzeScopeCommand"/>: resolves the scope and queues it through <see cref="AnalyzeItemsCommand"/>,
/// so the count of a <see cref="ResolveAnalysisScopeQuery"/> is the count queued.
/// </summary>
/// <param name="sender">Sends the query and the job command.</param>
public sealed class AnalyzeScopeCommandHandler(ISender sender) : IRequestHandler<AnalyzeScopeCommand, AnalyzeScopeResult>
{
    /// <inheritdoc />
    public async Task<AnalyzeScopeResult> Handle(AnalyzeScopeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var items = await sender.Send(new ResolveAnalysisScopeQuery(request.Scope), cancellationToken).ConfigureAwait(false);
        if (items.Count == 0)
        {
            return new AnalyzeScopeResult(null, 0);
        }

        var description = request.Description ?? Describe(request.Scope, items.Count);
        var jobId = await sender.Send(new AnalyzeItemsCommand(items.ItemIds, request.Analyzers, description), cancellationToken).ConfigureAwait(false);
        return new AnalyzeScopeResult(jobId, items.Count);
    }

    private static string? Describe(AnalysisScope scope, int count) => scope switch
    {
        AnalysisScope.PendingScope => $"Analyze {count} pending items",
        AnalysisScope.SearchResultsScope => $"Analyze {count} search results",
        AnalysisScope.FolderScope => $"Analyze {count} items in a folder",
        _ => null,
    };
}
