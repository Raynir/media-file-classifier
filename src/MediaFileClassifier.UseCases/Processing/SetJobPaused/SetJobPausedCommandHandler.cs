using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediatR;

namespace MediaFileClassifier.UseCases.Processing.SetJobPaused;

/// <summary>
/// Handles <see cref="SetJobPausedCommand"/> for the open library.
/// </summary>
/// <param name="jobs">Runs the jobs of the open library.</param>
public sealed class SetJobPausedCommandHandler(IJobSystem jobs) : IRequestHandler<SetJobPausedCommand, bool>
{
    /// <inheritdoc />
    /// <returns><see langword="true"/> when the job changed; <see langword="false"/> when it was already in that state, finished, or there's no library open.</returns>
    public async Task<bool> Handle(SetJobPausedCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (jobs.Current is not { } scheduler)
        {
            return false;
        }

        return request.Paused
            ? await scheduler.PauseJobAsync(request.JobId, cancellationToken).ConfigureAwait(false)
            : await scheduler.ResumeJobAsync(request.JobId, cancellationToken).ConfigureAwait(false);
    }
}
