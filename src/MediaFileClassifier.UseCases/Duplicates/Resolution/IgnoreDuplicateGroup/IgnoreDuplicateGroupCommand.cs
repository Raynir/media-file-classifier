using MediaFileClassifier.Domain.Duplicates;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Duplicates.Resolution.IgnoreDuplicateGroup;

/// <summary>
/// Marks an open duplicate group as not duplicates (E12-S04-T01): it is Ignored, so it is no longer suggested and a
/// detection pass leaves it so; only a new member opens it again (D578). The log gets an entry, which "Mark as duplicates
/// again" undoes.
/// </summary>
/// <param name="GroupId">The group.</param>
public sealed record IgnoreDuplicateGroupCommand(Id<DuplicateGroup> GroupId) : IRequest<Id<DuplicateResolution>?>;

/// <summary>
/// Handles <see cref="IgnoreDuplicateGroupCommand"/>.
/// </summary>
/// <param name="session">Holds the open library.</param>
/// <param name="clock">Gives the time.</param>
/// <param name="logger">Logs what was ignored.</param>
public sealed class IgnoreDuplicateGroupCommandHandler(ILibrarySession session, TimeProvider clock, ILogger<IgnoreDuplicateGroupCommandHandler> logger)
    : IRequestHandler<IgnoreDuplicateGroupCommand, Id<DuplicateResolution>?>
{
    /// <summary>
    /// Ignores the group.
    /// </summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The log entry, or <see langword="null"/> when the group doesn't exist or isn't open.</returns>
    public async Task<Id<DuplicateResolution>?> Handle(IgnoreDuplicateGroupCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (session.Current is not { } library
            || await library.Duplicates.GetAsync(request.GroupId, cancellationToken).ConfigureAwait(false) is not { State: DuplicateGroupState.Open } group)
        {
            return null;
        }

        var members = await library.Duplicates.GetMembersAsync(group.Id, cancellationToken).ConfigureAwait(false);
        var paths = await DuplicateMemberPaths.GetAsync(library, [.. members.Select(member => member.ItemId)], cancellationToken).ConfigureAwait(false);
        var resolution = DuplicateResolution.Ignored(group, clock.GetUtcNow());
        List<DuplicateResolutionMember> rows =
        [
            .. members.Select(member => new DuplicateResolutionMember(
                resolution.Id,
                member.ItemId,
                paths.TryGetValue(member.ItemId, out var path) ? path.DisplayPath : string.Empty,
                DuplicateMemberAction.Ignored)),
        ];
        if (!await library.DuplicateResolutions.AddAsync(resolution, rows, DuplicateGroupState.Ignored, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        logger.LogInformation("Marked duplicate group {Group} ({Count} members) as not duplicates", group.Id, rows.Count);
        return resolution.Id;
    }
}
