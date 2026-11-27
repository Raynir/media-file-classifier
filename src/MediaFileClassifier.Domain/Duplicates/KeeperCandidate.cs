using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Domain.Duplicates;

/// <summary>
/// A group member as the keeper rules see it (E12-S03-T03).
/// </summary>
/// <param name="ItemId">The item.</param>
/// <param name="UserMetadataCount">How much the user added: tags, edited fields and the favorite mark.</param>
/// <param name="Pixels">Width times height, or 0 when unknown.</param>
/// <param name="SizeBytes">The file size.</param>
/// <param name="Format">The file format, e.g. <c>.jpg</c>; the dot and the case don't matter.</param>
/// <param name="FileCreatedAt">When the file was created.</param>
/// <param name="IsAvailable">Whether the file can be kept: a Missing or offline member can't be the keeper.</param>
public sealed record KeeperCandidate(
    Id<MediaItem> ItemId,
    int UserMetadataCount,
    long Pixels,
    long SizeBytes,
    string Format,
    DateTimeOffset FileCreatedAt,
    bool IsAvailable = true);

/// <summary>
/// The member suggested to keep.
/// </summary>
/// <param name="ItemId">The suggested member.</param>
/// <param name="DecidingRule">The rule that left it as the only candidate, or <see langword="null"/> when no rule
/// could tell the members apart (the first was taken) or only one member could be kept.</param>
public sealed record KeeperSuggestion(Id<MediaItem> ItemId, KeeperRule? DecidingRule);
