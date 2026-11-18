using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediatR;

namespace MediaFileClassifier.UseCases.Analysis.UpdateAnalysisProfile;

/// <summary>
/// Replaces the open library's analysis profile (E06-S01-T01). Items analyzed from then on use it; items already
/// analyzed keep their results until they're analyzed again. The settings UI is E17.
/// </summary>
/// <param name="Profile">The new profile.</param>
public sealed record UpdateAnalysisProfileCommand(AnalysisProfile Profile) : IRequest;

/// <summary>
/// Handles <see cref="UpdateAnalysisProfileCommand"/>.
/// </summary>
/// <param name="session">The library session.</param>
public sealed class UpdateAnalysisProfileCommandHandler(ILibrarySession session)
    : IRequestHandler<UpdateAnalysisProfileCommand>
{
    /// <inheritdoc />
    public async Task Handle(UpdateAnalysisProfileCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Profile);

        var open = session.Current ?? throw new InvalidOperationException("No library is open.");
        await open.UpdateSettingsAsync(settings => settings with { AnalysisProfile = request.Profile }, cancellationToken).ConfigureAwait(false);
    }
}
