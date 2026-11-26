using MediaFileClassifier.Infrastructure.Abstractions.Files;

namespace MediaFileClassifier.Testing.Files;

/// <summary>
/// An <see cref="IFileTrash"/> that touches no file: every path is reported as moved unless <see cref="Results"/> sets
/// another result for it. Records the paths it was given.
/// </summary>
public sealed class FakeFileTrash : IFileTrash
{
    private readonly List<string> moved = [];

    /// <summary>
    /// Gets the results to report for given paths, e.g. <c>TrashResult.Skipped(path, TrashSkipReason.NoTrash)</c>.
    /// Paths compare ignoring case, as on Windows.
    /// </summary>
    public Dictionary<string, TrashResult> Results { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets every call's paths, in order.
    /// </summary>
    public List<IReadOnlyList<string>> Calls { get; } = [];

    /// <summary>
    /// Gets the paths reported as moved so far.
    /// </summary>
    public IReadOnlyList<string> Moved => moved;

    /// <inheritdoc />
    public Task<IReadOnlyList<TrashResult>> MoveToTrashAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        Calls.Add([.. paths]);
        var results = new List<TrashResult>(paths.Count);
        foreach (var path in paths)
        {
            var result = cancellationToken.IsCancellationRequested
                ? TrashResult.Skipped(path, TrashSkipReason.Cancelled)
                : Results.GetValueOrDefault(path) ?? TrashResult.Moved(path);
            if (result.Outcome == TrashOutcome.Moved)
            {
                moved.Add(path);
            }

            results.Add(result);
        }

        return Task.FromResult<IReadOnlyList<TrashResult>>(results);
    }
}
