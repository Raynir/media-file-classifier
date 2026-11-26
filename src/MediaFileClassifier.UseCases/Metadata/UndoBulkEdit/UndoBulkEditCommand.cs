using MediaFileClassifier.Domain.Activity;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.UseCases.Activity.RecordActivity;
using MediatR;

namespace MediaFileClassifier.UseCases.Metadata.UndoBulkEdit;

/// <summary>
/// Puts back what the last bulk edit changed (E07-S03-T03): every item's field values, tags, tombstones and AI-value
/// state as they were before it. Items edited since are overwritten too. The operation can be undone once.
/// </summary>
public sealed record UndoBulkEditCommand : IRequest<UndoBulkEditResult>;

/// <summary>
/// The outcome of <see cref="UndoBulkEditCommand"/>.
/// </summary>
/// <param name="RestoredItemIds">The items that were put back; none if there was nothing to undo.</param>
public sealed record UndoBulkEditResult(IReadOnlyList<Id<MediaItem>> RestoredItemIds);

/// <summary>
/// Handles <see cref="UndoBulkEditCommand"/>: writes the snapshots back through the batched write path and publishes
/// one <see cref="ItemsMetadataChanged"/>.
/// </summary>
/// <param name="session">Holds the open library.</param>
/// <param name="store">The stored operation.</param>
/// <param name="publisher">Publishes the change.</param>
/// <param name="sender">Records the event in the activity log, or <see langword="null"/> for none.</param>
public sealed class UndoBulkEditCommandHandler(ILibrarySession session, BulkUndoStore store, IPublisher publisher, ISender? sender = null)
    : IRequestHandler<UndoBulkEditCommand, UndoBulkEditResult>
{
    /// <inheritdoc />
    public async Task<UndoBulkEditResult> Handle(UndoBulkEditCommand request, CancellationToken cancellationToken)
    {
        if (session.Current is not { } library)
        {
            return new UndoBulkEditResult([]);
        }

        var snapshots = store.Take();
        if (snapshots.Count == 0)
        {
            return new UndoBulkEditResult([]);
        }

        var written = new List<Id<MediaItem>>();
        try
        {
            // Not cancellable: half an undo would leave the items in a mix that no one chose.
            await library.Metadata.UpdateManyAsync(
                [.. snapshots.Keys],
                metadata =>
                {
                    metadata.RestoreUserLayer(snapshots[metadata.ItemId]);
                    return true;
                },
                written.AddRange,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // What wasn't put back stays undoable, so the user can try again.
            var done = written.ToHashSet();
            store.Set(snapshots.Where(pair => !done.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value));
            throw;
        }
        finally
        {
            if (written.Count > 0)
            {
                await publisher.Publish(
                    new ItemsMetadataChanged(written, ItemMetadataChange.Fields | ItemMetadataChange.Tags)
                    {
                        Fields = Enum.GetValues<Domain.Metadata.DescriptiveField>().ToHashSet(),
                    },
                    CancellationToken.None).ConfigureAwait(false);
                if (sender is not null)
                {
                    await sender.Send(
                        new RecordActivityCommand(
                            ActivityEventType.BulkEdit,
                            Counts: new Dictionary<string, long> { [ActivityCounts.Restored] = written.Count }),
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        return new UndoBulkEditResult(written);
    }
}
