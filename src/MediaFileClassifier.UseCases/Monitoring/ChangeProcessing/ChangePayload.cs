using System.Text.Json;
using System.Text.Json.Serialization;
using MediaFileClassifier.Infrastructure.Abstractions.Files;

namespace MediaFileClassifier.UseCases.Monitoring.ChangeProcessing;

/// <summary>
/// The payload of a <see cref="ChangeProcessingStages.Apply"/> work item: a batch of a watched folder's changes. Stored
/// as JSON with the work item, so the shape never changes.
/// </summary>
/// <param name="Changes">The changes, in the order they became stable.</param>
public sealed record ChangePayload([property: JsonPropertyName("changes")] IReadOnlyList<ChangePayload.Entry> Changes)
{
    /// <summary>
    /// Writes a batch of changes.
    /// </summary>
    /// <param name="changes">The changes.</param>
    /// <returns>The JSON.</returns>
    public static string Serialize(IEnumerable<FolderChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        return JsonSerializer.Serialize(new ChangePayload([.. changes.Select(Entry.From)]));
    }

    /// <summary>
    /// Reads a batch of changes.
    /// </summary>
    /// <param name="payload">The work item's payload.</param>
    /// <returns>The changes; none when there's no payload.</returns>
    /// <exception cref="JsonException">The payload isn't a change payload.</exception>
    public static IReadOnlyList<FolderChange> Deserialize(string? payload) =>
        string.IsNullOrWhiteSpace(payload)
            ? []
            : [.. (JsonSerializer.Deserialize<ChangePayload>(payload)?.Changes ?? []).Select(entry => entry.ToChange())];

    /// <summary>
    /// One change. The kind is stored by name, so the enum can grow.
    /// </summary>
    /// <param name="Kind">The <see cref="FolderChangeKind"/> name.</param>
    /// <param name="Path">The file's path; for a rename, its new path.</param>
    /// <param name="OldPath">For a rename, the old path.</param>
    /// <param name="Length">The file's size when it was found stable, if it exists.</param>
    /// <param name="Modified">The file's modification time when it was found stable, if it exists.</param>
    /// <param name="Created">The file's creation time, if known.</param>
    public sealed record Entry(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("oldPath")] string? OldPath = null,
        [property: JsonPropertyName("length")] long? Length = null,
        [property: JsonPropertyName("modified")] DateTimeOffset? Modified = null,
        [property: JsonPropertyName("created")] DateTimeOffset? Created = null)
    {
        internal static Entry From(FolderChange change) =>
            new(change.Kind.ToString(), change.RelativePath, change.OldRelativePath, change.Stat?.Length, change.Stat?.LastModifiedUtc, change.Stat?.CreatedUtc);

        internal FolderChange ToChange()
        {
            if (!Enum.TryParse<FolderChangeKind>(Kind, out var kind))
            {
                throw new JsonException($"Unknown change kind '{Kind}'.");
            }

            var stat = Length is { } length && Modified is { } modified
                ? new MediaFileStat(Path, length, modified) { CreatedUtc = Created }
                : null;
            return new FolderChange(kind, Path, OldPath, stat);
        }
    }
}
