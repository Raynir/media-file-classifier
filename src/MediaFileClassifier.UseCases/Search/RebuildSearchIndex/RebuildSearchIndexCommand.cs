using System.Diagnostics;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Search.RebuildSearchIndex;

/// <summary>
/// Rebuilds the open library's keyword and vector indexes from the source tables (E01-S04-T06), for "Rebuild or
/// refresh the index" (spec 1.2). Searches keep working while it runs.
/// </summary>
/// <param name="Progress">Receives the share done, from 0 to 1, or <c>null</c>.</param>
public sealed record RebuildSearchIndexCommand(IProgress<double>? Progress = null) : IRequest;

/// <summary>
/// Handles <see cref="RebuildSearchIndexCommand"/>.
/// </summary>
/// <param name="session">Holds the open library.</param>
/// <param name="index">The search index.</param>
/// <param name="logger">Logger.</param>
public sealed class RebuildSearchIndexCommandHandler(
    ILibrarySession session,
    ISearchIndex index,
    ILogger<RebuildSearchIndexCommandHandler> logger) : IRequestHandler<RebuildSearchIndexCommand>
{
    /// <inheritdoc />
    public async Task Handle(RebuildSearchIndexCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var library = session.Current ?? throw new InvalidOperationException("No library is open.");
        var started = Stopwatch.GetTimestamp();
        logger.LogInformation("Rebuilding the search index of {Library}", library.Library.Name);
        await index.RebuildAsync(request.Progress, cancellationToken).ConfigureAwait(false);
        logger.LogInformation(
            "Rebuilt the search index of {Library} in {ElapsedMs} ms",
            library.Library.Name,
            (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }
}
