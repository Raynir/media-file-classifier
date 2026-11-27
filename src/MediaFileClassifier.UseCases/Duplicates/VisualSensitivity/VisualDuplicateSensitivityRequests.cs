using MediaFileClassifier.Domain.Duplicates;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.UseCases.Duplicates.DetectDuplicates;
using MediatR;

namespace MediaFileClassifier.UseCases.Duplicates.VisualSensitivity;

/// <summary>
/// Gets the open library's visual near-duplicate sensitivity (E12-S02-T03), or <see langword="null"/> when no library
/// is open.
/// </summary>
public sealed record GetVisualDuplicateSensitivityQuery : IRequest<VisualDuplicateSensitivity?>;

/// <summary>
/// Changes the open library's visual near-duplicate sensitivity (E12-S02-T03) and checks every item again with it: a
/// full pass as a background job, or, while one is already running, by queueing every item for the
/// <see cref="DuplicateGroupMaintainer"/>.
/// </summary>
/// <param name="Sensitivity">The new sensitivity.</param>
public sealed record SetVisualDuplicateSensitivityCommand(VisualDuplicateSensitivity Sensitivity) : IRequest<SetVisualDuplicateSensitivityResult>;

/// <summary>
/// What <see cref="SetVisualDuplicateSensitivityCommand"/> did.
/// </summary>
/// <param name="Changed">Whether the sensitivity changed; <see langword="false"/> when it was already set or no library is open.</param>
/// <param name="Detection">How the full pass started, when it changed.</param>
public sealed record SetVisualDuplicateSensitivityResult(bool Changed, DetectDuplicatesOutcome? Detection = null);

/// <summary>
/// Handles <see cref="GetVisualDuplicateSensitivityQuery"/> and <see cref="SetVisualDuplicateSensitivityCommand"/>.
/// </summary>
/// <param name="session">Holds the open library.</param>
/// <param name="sender">Starts the full pass.</param>
/// <param name="maintainer">Checks the queued items when no job does.</param>
public sealed class VisualDuplicateSensitivityHandlers(ILibrarySession session, ISender sender, DuplicateGroupMaintainer? maintainer = null)
    : IRequestHandler<GetVisualDuplicateSensitivityQuery, VisualDuplicateSensitivity?>,
        IRequestHandler<SetVisualDuplicateSensitivityCommand, SetVisualDuplicateSensitivityResult>
{
    /// <inheritdoc />
    public Task<VisualDuplicateSensitivity?> Handle(GetVisualDuplicateSensitivityQuery request, CancellationToken cancellationToken) =>
        Task.FromResult(session.Current?.Library.Settings.VisualDuplicateSensitivity);

    /// <inheritdoc />
    public async Task<SetVisualDuplicateSensitivityResult> Handle(SetVisualDuplicateSensitivityCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Sensitivity))
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Sensitivity, "Unknown sensitivity.");
        }

        if (session.Current is not { } open || open.Library.Settings.VisualDuplicateSensitivity == request.Sensitivity)
        {
            return new(false);
        }

        await open.UpdateSettingsAsync(settings => settings with { VisualDuplicateSensitivity = request.Sensitivity }, cancellationToken).ConfigureAwait(false);

        // Every group may change, so every item is checked again.
        DetectDuplicatesOutcome outcome;
        try
        {
            outcome = (await sender.Send(new DetectDuplicatesCommand(), cancellationToken).ConfigureAwait(false)).Outcome;
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
        {
            // The setting is stored, so its change mustn't be lost: the maintainer checks every item instead.
            outcome = DetectDuplicatesOutcome.Unavailable;
        }

        if (outcome is DetectDuplicatesOutcome.Busy or DetectDuplicatesOutcome.Unavailable)
        {
            // A running pass has a fixed number of batches, so the maintainer works off what it leaves.
            await open.VisualDuplicates.QueueAllAsync(cancellationToken).ConfigureAwait(false);
            maintainer?.Request();
        }

        return new(true, outcome);
    }
}
