using MediaFileClassifier.Domain.Activity;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Activity.RecordActivity;

/// <summary>
/// Records one event in the activity log of the open library (E15-S04). Features call it when something significant
/// happened, such as folder monitoring picking up changes (MFC-140). It never fails the caller: the log is a record
/// of what happened, so a write that fails is logged and dropped.
/// </summary>
/// <param name="Type">What happened.</param>
/// <param name="Severity">How it ended.</param>
/// <param name="Subject">What it happened to, such as a folder path, or <see langword="null"/>.</param>
/// <param name="Counts">Named numbers, see <see cref="ActivityCounts"/>, or <see langword="null"/>.</param>
/// <param name="FolderId">The folder it concerns, so the log can open it, or <see langword="null"/>.</param>
/// <param name="ItemId">The one item it concerns, so the log can open it, or <see langword="null"/>.</param>
public sealed record RecordActivityCommand(
    ActivityEventType Type,
    ActivitySeverity Severity = ActivitySeverity.Info,
    string? Subject = null,
    IReadOnlyDictionary<string, long>? Counts = null,
    Id<IndexedFolder>? FolderId = null,
    Id<MediaItem>? ItemId = null) : IRequest;

/// <summary>
/// Handles <see cref="RecordActivityCommand"/>.
/// </summary>
/// <param name="session">The open library.</param>
/// <param name="clock">The clock.</param>
/// <param name="changes">Tells the views.</param>
/// <param name="logger">Logs a write that failed.</param>
public sealed partial class RecordActivityCommandHandler(
    ILibrarySession session,
    TimeProvider clock,
    ActivityChanges changes,
    ILogger<RecordActivityCommandHandler> logger) : IRequestHandler<RecordActivityCommand>
{
    /// <inheritdoc />
    public async Task Handle(RecordActivityCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (session.Current is not { } library)
        {
            return;
        }

        try
        {
            var activity = ActivityEvent.Create(
                request.Type,
                request.Severity,
                clock.GetUtcNow(),
                request.Subject,
                request.Counts,
                request.FolderId,
                request.ItemId);
            await library.Activity.AddAsync(activity, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, ex, request.Type);
            return;
        }

        changes.Raise();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not record the {Type} activity")]
    private static partial void LogFailed(ILogger logger, Exception exception, ActivityEventType type);
}
