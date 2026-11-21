using System.Diagnostics;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Maintenance.CheckIntegrity;

/// <summary>
/// Runs the open library's database integrity check (E01-S03-T07) for "Check library integrity" (E02-S06-T01).
/// Reads and writes carry on meanwhile.
/// </summary>
public sealed record CheckLibraryIntegrityQuery : IRequest<IntegrityReport>;

/// <summary>
/// The result of <see cref="CheckLibraryIntegrityQuery"/>.
/// </summary>
/// <param name="Problems">What SQLite reported; empty when the database is intact.</param>
public sealed record IntegrityReport(IReadOnlyList<string> Problems)
{
    /// <summary>
    /// Gets a value indicating whether the database is intact.
    /// </summary>
    public bool IsOk => Problems.Count == 0;
}

/// <summary>
/// Handles <see cref="CheckLibraryIntegrityQuery"/>.
/// </summary>
/// <param name="session">Holds the open library.</param>
/// <param name="logger">Logger.</param>
public sealed class CheckLibraryIntegrityQueryHandler(ILibrarySession session, ILogger<CheckLibraryIntegrityQueryHandler> logger)
    : IRequestHandler<CheckLibraryIntegrityQuery, IntegrityReport>
{
    /// <summary>
    /// How many of SQLite's messages are logged.
    /// </summary>
    internal const int LoggedProblems = 20;

    /// <inheritdoc />
    public async Task<IntegrityReport> Handle(CheckLibraryIntegrityQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var library = session.Current ?? throw new InvalidOperationException("No library is open.");
        var started = Stopwatch.GetTimestamp();
        var result = await library.Maintenance.CheckIntegrityAsync(cancellationToken).ConfigureAwait(false);
        var elapsed = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (result.IsOk)
        {
            logger.LogInformation("The integrity check of {Library} found no problems in {ElapsedMs} ms", library.Library.Name, elapsed);
        }
        else
        {
            logger.LogError(
                "The integrity check of {Library} found {Count} problems in {ElapsedMs} ms: {Problems}",
                library.Library.Name,
                result.Problems.Count,
                elapsed,
                string.Join(" | ", result.Problems.Take(LoggedProblems)));
        }

        return new IntegrityReport(result.Problems);
    }
}
