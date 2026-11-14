using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediatR;

namespace MediaFileClassifier.UseCases.Processing.GetJobFailures;

/// <summary>
/// Handles <see cref="GetJobFailuresQuery"/> for the open library.
/// </summary>
/// <param name="session">Holds the open library.</param>
public sealed class GetJobFailuresQueryHandler(ILibrarySession session) : IRequestHandler<GetJobFailuresQuery, IReadOnlyList<FailedWorkItem>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<FailedWorkItem>> Handle(GetJobFailuresQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (session.Current is not { } library)
        {
            return [];
        }

        var failed = await library.Jobs.GetFailedWorkItemsAsync(request.JobId, cancellationToken).ConfigureAwait(false);
        var itemIds = failed.Where(work => work.ItemId is not null).Select(work => work.ItemId!.Value).Distinct().ToList();
        var paths = itemIds.Count == 0
            ? []
            : (await library.Items.GetPathsAsync(itemIds, cancellationToken).ConfigureAwait(false)).ToDictionary(path => path.Id, path => path.Path);

        return [.. failed.Select(work =>
        {
            RelativePath? path = work.ItemId is { } id && paths.TryGetValue(id, out var found) ? found : null;
            return new FailedWorkItem(work.Id, work.ItemId, path?.FileName, path?.Value, work.Stage, work.LastError ?? string.Empty, work.ErrorCategory);
        })];
    }
}
