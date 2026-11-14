using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediatR;

namespace MediaFileClassifier.UseCases.Processing.CancelJob;

/// <summary>
/// Handles <see cref="CancelJobCommand"/> for the open library.
/// </summary>
/// <param name="jobs">Runs the jobs of the open library.</param>
public sealed class CancelJobCommandHandler(IJobSystem jobs) : IRequestHandler<CancelJobCommand, bool>
{
    /// <inheritdoc />
    /// <returns><see langword="true"/> when the job was cancelled; <see langword="false"/> when it had finished already or there's no library open.</returns>
    public async Task<bool> Handle(CancelJobCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return jobs.Current is { } scheduler
            && await scheduler.CancelJobAsync(request.JobId, cancellationToken).ConfigureAwait(false);
    }
}
