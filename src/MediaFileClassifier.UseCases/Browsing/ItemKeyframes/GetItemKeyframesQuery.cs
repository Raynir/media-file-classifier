using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediatR;

namespace MediaFileClassifier.UseCases.Browsing.ItemKeyframes;

/// <summary>
/// A keyframe sampled from a video during analysis (E06-S02), as the preview shows it: a marker on the timeline
/// (E14-S06) and, later, a frame in the keyframe strip (E14-S13). The keyframe's ID and index name its frame
/// thumbnail file, next to the item's thumbnails (D227).
/// </summary>
/// <param name="KeyframeId">The keyframe.</param>
/// <param name="Index">Its position among the item's keyframes, from 0.</param>
/// <param name="Timestamp">Where it is in the video.</param>
public sealed record KeyframeMarker(Id<Keyframe> KeyframeId, int Index, TimeSpan Timestamp);

/// <summary>
/// Gets an item's keyframes for the preview (E14-S06-T02).
/// </summary>
/// <param name="ItemId">The item.</param>
public sealed record GetItemKeyframesQuery(Id<MediaItem> ItemId) : IRequest<IReadOnlyList<KeyframeMarker>>;

/// <summary>
/// Handles <see cref="GetItemKeyframesQuery"/> for the open library.
/// </summary>
/// <param name="session">Holds the open library.</param>
public sealed class GetItemKeyframesQueryHandler(ILibrarySession session) : IRequestHandler<GetItemKeyframesQuery, IReadOnlyList<KeyframeMarker>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<KeyframeMarker>> Handle(GetItemKeyframesQuery request, CancellationToken cancellationToken)
    {
        if (session.Current is not { } library)
        {
            return [];
        }

        var keyframes = await library.Keyframes.GetForItemAsync(request.ItemId, cancellationToken).ConfigureAwait(false);
        return [.. keyframes.OrderBy(keyframe => keyframe.Timestamp).Select(keyframe => new KeyframeMarker(keyframe.Id, keyframe.Index, keyframe.Timestamp))];
    }
}
